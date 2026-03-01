// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Data.Types;

namespace Email;

[TestClass]
public class EmailAddressTests
{
    #region Constructor (user, domain)

    [TestMethod]
    public void Constructor_UserDomain_SetsProperties()
    {
        // Arrange & Act
        var email = new EmailAddress("fox", "example.com");

        // Assert
        Assert.AreEqual("fox", email.User);
        Assert.AreEqual("example.com", email.Domain);
    }

    [TestMethod]
    public void Full_ReturnsUserAtDomain()
    {
        // Arrange
        var email = new EmailAddress("fox", "example.com");

        // Act & Assert
        Assert.AreEqual("fox@example.com", email.Full);
    }

    [TestMethod]
    public void ToString_ReturnsFull()
    {
        // Arrange
        var email = new EmailAddress("admin", "hive.com");

        // Act & Assert
        Assert.AreEqual("admin@hive.com", email.ToString());
    }

    #endregion

    #region Constructor (email string)

    [TestMethod]
    public void Constructor_EmailString_ParsesCorrectly()
    {
        // Arrange & Act
        var email = new EmailAddress("user@domain.org");

        // Assert
        Assert.AreEqual("user", email.User);
        Assert.AreEqual("domain.org", email.Domain);
    }

    [TestMethod]
    public void Constructor_EmailString_SubdomainParsesCorrectly()
    {
        // Arrange & Act
        var email = new EmailAddress("postmaster@mail.example.co.uk");

        // Assert
        Assert.AreEqual("postmaster", email.User);
        Assert.AreEqual("mail.example.co.uk", email.Domain);
    }

    [TestMethod]
    public void Constructor_EmailString_WithPlusAddressing()
    {
        // Arrange & Act
        var email = new EmailAddress("user+tag@example.com");

        // Assert
        Assert.AreEqual("user+tag", email.User);
        Assert.AreEqual("example.com", email.Domain);
    }

    [TestMethod]
    public void Constructor_EmailString_WithDottedUser()
    {
        // Arrange & Act
        var email = new EmailAddress("first.last@example.com");

        // Assert
        Assert.AreEqual("first.last", email.User);
        Assert.AreEqual("example.com", email.Domain);
    }

    [TestMethod]
    public void Constructor_EmailString_InvalidFormat_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress("not-an-email"));
    }

    [TestMethod]
    public void Constructor_EmailString_Empty_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => new EmailAddress(""));
    }

    #endregion

    #region ParseFromSmtp

    [TestMethod]
    public void ParseFromSmtp_AngleBrackets_ParsesCorrectly()
    {
        // Arrange & Act
        var email = EmailAddress.ParseFromSmtp("MAIL FROM:<user@example.com>");

        // Assert
        Assert.AreEqual("user", email.User);
        Assert.AreEqual("example.com", email.Domain);
    }

    [TestMethod]
    public void ParseFromSmtp_RcptTo_ParsesCorrectly()
    {
        // Arrange & Act
        var email = EmailAddress.ParseFromSmtp("RCPT TO:<recipient@hive.com>");

        // Assert
        Assert.AreEqual("recipient", email.User);
        Assert.AreEqual("hive.com", email.Domain);
    }

    [TestMethod]
    public void ParseFromSmtp_BareAngleBrackets_ParsesCorrectly()
    {
        // Arrange & Act
        var email = EmailAddress.ParseFromSmtp("<admin@server.net>");

        // Assert
        Assert.AreEqual("admin", email.User);
        Assert.AreEqual("server.net", email.Domain);
    }

    [TestMethod]
    public void ParseFromSmtp_NoBrackets_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp("plaintext without email"));
    }

    [TestMethod]
    public void ParseFromSmtp_Empty_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() => EmailAddress.ParseFromSmtp(""));
    }

    [TestMethod]
    public void ParseFromSmtp_Full_MatchesToString()
    {
        // Arrange & Act
        var email = EmailAddress.ParseFromSmtp("<test@test.com>");

        // Assert
        Assert.AreEqual("test@test.com", email.Full);
        Assert.AreEqual("test@test.com", email.ToString());
    }

    #endregion
}
