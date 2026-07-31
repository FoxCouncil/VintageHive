// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Reflection;
using System.Text;
using VintageHive.Proxy.Yahoo;

namespace Yahoo;

// Known-answer tests for the YMSG v9 "0x0b" auth crypt.
//
// The expected values in TestData/ymsg-auth-vectors.tsv were not produced by this code. They come from compiling
// the reference C implementation and running it, so these tests are an external check on the port rather than a
// round trip through our own assumptions. Every vector was screened to make sure the reference decoded a complete
// 20-byte comparison block without reading past its own parsed data - seeds that fail that screen make the
// reference read uninitialised stack, and pinning one would pin garbage.
[TestClass]
public class YmsgCryptTests
{
    private sealed record Vector(string Seed, string Password, string Resp6, string Resp96, string MagicKey, int Depth, int Table, int J);

    private static List<Vector> LoadVectors()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith("TestData.ymsg-auth-vectors.tsv", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var vectors = new List<Vector>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t');

            vectors.Add(new Vector(parts[0], parts[1], parts[2], parts[3], parts[4], int.Parse(parts[5]), int.Parse(parts[6]), int.Parse(parts[7])));
        }

        return vectors;
    }

    [TestMethod]
    public void Vectors_AreLoaded()
    {
        var vectors = LoadVectors();

        Assert.IsTrue(vectors.Count >= 20, $"Expected the pinned vector set, got {vectors.Count} rows.");
    }

    [TestMethod]
    public void ComputeAuthResponse_MatchesReferenceVectors()
    {
        var failures = new List<string>();

        foreach (var vector in LoadVectors())
        {
            var actual = YmsgCrypt.ComputeAuthResponse(vector.Seed, vector.Password);

            if (actual == null)
            {
                failures.Add($"[{vector.Password}] returned null (refused a seed the reference processed)");

                continue;
            }

            if (actual.Resp6 != vector.Resp6)
            {
                failures.Add($"[{vector.Password}] resp6 expected {vector.Resp6} got {actual.Resp6}");
            }

            if (actual.Resp96 != vector.Resp96)
            {
                failures.Add($"[{vector.Password}] resp96 expected {vector.Resp96} got {actual.Resp96}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    // The intermediates are pinned separately so a mismatch says which stage broke rather than just "the answer
    // is wrong" - the seed parse, the transform search, and the hash dance fail in very different ways.
    [TestMethod]
    public void ComputeAuthResponse_MatchesReferenceIntermediates()
    {
        foreach (var vector in LoadVectors())
        {
            var actual = YmsgCrypt.ComputeAuthResponse(vector.Seed, vector.Password);

            Assert.IsNotNull(actual);

            Assert.AreEqual(vector.MagicKey, Convert.ToHexString(actual.MagicKey).ToLowerInvariant(), "magic key derived from the seed differs");
            Assert.AreEqual(vector.Depth, actual.Depth, "transform depth differs");
            Assert.AreEqual(vector.Table, actual.Table, "transform table differs");
            Assert.AreEqual(vector.J, actual.JFinal, "loop-exit j differs - this drives the SHA-1 length poke");
        }
    }

    [TestMethod]
    public void ComputeAuthResponse_DifferentPasswords_ProduceDifferentResponses()
    {
        var vectors = LoadVectors();

        var seed = vectors[0].Seed;

        var a = YmsgCrypt.ComputeAuthResponse(seed, "hunter2");
        var b = YmsgCrypt.ComputeAuthResponse(seed, "hunter3");

        Assert.AreNotEqual(a.Resp6, b.Resp6);
        Assert.AreNotEqual(a.Resp96, b.Resp96);
    }

    // MakeChallenge must emit what a real server emitted: a seed whose embedded (depth, table) pair the client's
    // brute-force search actually finds. A real Messenger 5.5 answers a seed whose search never matches with
    // EMPTY fields 6 and 96 - no login can ever succeed - so the found-at-first-candidate shape is the fix for
    // that, not a nicety. Depth 0 / table 0 / loop-exit j 1 is the one combination every client lineage computes
    // identically (no yahoo_xfrm, no SHA-1 length poke).
    [TestMethod]
    public void MakeChallenge_EmbedsTransformPairTheClientSearchFinds()
    {
        for (var i = 0; i < 8; i++)
        {
            var seed = YmsgCrypt.MakeChallenge();

            Assert.IsNotNull(seed, "MakeChallenge failed its own round-trip check");

            var state = YmsgCrypt.PrepareChallenge(seed);

            Assert.IsNotNull(state);
            Assert.AreEqual(0, state.Depth);
            Assert.AreEqual(0, state.Table);
            Assert.AreEqual(1, state.JFinal, "loop-exit j must be 1: the search has to match on the client's very first candidate");

            foreach (var c in seed)
            {
                Assert.IsTrue(YmsgCrypt.ChallengeLookup.Contains(c) || YmsgCrypt.OperandLookup.Contains(c) || c == '(' || c == ')', $"character '{c}' in seed '{seed}' is outside the client's alphabets");
            }
        }
    }

    // A seed carrying anything outside the challenge and operand alphabets makes the reference client spin
    // forever, because it does not advance its read pointer on an unknown character. We refuse instead.
    [TestMethod]
    public void ComputeAuthResponse_UnprocessableSeed_ReturnsNull()
    {
        Assert.IsNull(YmsgCrypt.ComputeAuthResponse("c=1A2B3C4D$vintagehive$", "hunter2"), "A seed a real client cannot parse must not yield a response.");
        Assert.IsNull(YmsgCrypt.ComputeAuthResponse("c=1a2b3c4d", "hunter2"));
        Assert.IsNull(YmsgCrypt.ComputeAuthResponse("", "hunter2"));
        Assert.IsNull(YmsgCrypt.ComputeAuthResponse(null, "hunter2"));
    }

    // Too short to decode a full 20-byte comparison block: the reference would carry on with undefined bytes.
    [TestMethod]
    public void ComputeAuthResponse_SeedTooShort_ReturnsNull()
    {
        Assert.IsNull(YmsgCrypt.ComputeAuthResponse("qzec+|&%", "hunter2"));
    }

    // The y64 alphabet is 65 characters long and the reference indexes [64] for padding, so a 16-byte digest
    // encodes to 24 characters ending "--" - not 22. Getting this wrong shifts the length of every XOR pad
    // downstream and silently changes both responses, which is exactly what it did the first time.
    [TestMethod]
    public void Y64_EncodesSixteenBytesAsTwentyFourPaddedCharacters()
    {
        var encoded = YmsgCrypt.Y64(new byte[16]);

        Assert.AreEqual(24, encoded.Length);
        Assert.AreEqual("AAAAAAAAAAAAAAAAAAAAAA--", encoded);
    }

    // Pinned from the reference: to_y64(md5("hunter2")) and to_y64(md5(yahoo_crypt("hunter2"))).
    [TestMethod]
    public void Y64_OfPasswordDigest_MatchesReference()
    {
        var passwordHash = YmsgCrypt.Y64(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes("hunter2")));

        Assert.AreEqual("KrljkMfb40Od500MmwsXZw--", passwordHash);
    }

    // MD5-crypt is its own trap - the bit-driven loop and the 1000-round stretch are easy to get subtly wrong,
    // so the exact string the reference produced is pinned rather than just its shape.
    [TestMethod]
    public void YahooCrypt_MatchesReference()
    {
        var hash = YmsgCrypt.YahooCrypt("hunter2", "$1$_2S43d5f$");

        Assert.AreEqual("$1$_2S43d5f$i9tT.Wsn4bKsHMNglJlKa1", hash);

        var cryptHash = YmsgCrypt.Y64(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(hash)));

        Assert.AreEqual("Mf8UHlVDmGZ4LACf5Xvf4w--", cryptHash);
    }
}
