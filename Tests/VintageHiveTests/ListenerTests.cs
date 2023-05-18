// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Moq;
using System.Net;
using System.Net.Sockets;
using VintageHive.Network;

namespace Network;

[TestClass]
public class ListenerTests
{
    [TestMethod]
    public void Constructor_Should_Properly_Set_Properties()
    {
        // Arrange
        var ipAddress = IPAddress.Parse("192.168.1.1");
        var port = 80;
        var socketType = SocketType.Stream;
        var protocolType = ProtocolType.Tcp;
        var secure = true;

        // Act
        var listener = new Mock<Listener>(ipAddress, port, socketType, protocolType, secure).Object;

        // Assert
        Assert.AreEqual(listener.Address, ipAddress);
        Assert.AreEqual(listener.Port, port);
        Assert.AreEqual(listener.SocketType, socketType);
        Assert.AreEqual(listener.ProtocolType, protocolType);
        Assert.AreEqual(listener.IsSecure, true);
    }

    [TestMethod]
    public void Start_Should_Start_ProcessThread()
    {
        // Arrange
        var listener = new Mock<Listener>(IPAddress.Loopback, 80, SocketType.Stream, ProtocolType.Tcp, false).Object;

        // Act
        listener.Start();

        // Assert
        Assert.AreEqual(listener.ProcessThread.IsAlive, true);
    }

    [TestMethod]
    public void Start_Should_Start_With_Correct_State()
    {
        // Arrange
        var port = 80;
        var secure = false;
        var listener = new Mock<Listener>(IPAddress.Loopback, port, SocketType.Stream, ProtocolType.Tcp, secure).Object;

        // Act
        listener.Start();

        // Assert
        Assert.IsTrue(listener.ProcessThread.IsAlive);
        Assert.AreEqual(listener.IsSecure, secure);
        Assert.AreEqual(listener.Port, port);
        Thread.Sleep(1); // ahah, prolly a better way?? 
        Assert.IsTrue(listener.IsListening);
    }

    [TestMethod]
    public void ProcessConnection_Should_Return_Null()
    {
        // Arrange
        var listener = new Mock<Listener>(IPAddress.Loopback, 80, SocketType.Stream, ProtocolType.Tcp, false).Object;
        var connection = new Mock<ListenerSocket>().Object;

        // Act
        var result = listener.ProcessConnection(connection).Result;

        // Assert
        Assert.IsInstanceOfType<byte[]>(result);
        Assert.IsTrue(result.Length == 0);
    }

    [TestMethod]
    public void ProcessRequest_Should_Return_Null()
    {
        // Arrange
        var listener = new Mock<Listener>(IPAddress.Loopback, 80, SocketType.Stream, ProtocolType.Tcp, false).Object;
        var connection = new Mock<ListenerSocket>().Object;

        // Act
        var result = listener.ProcessRequest(connection, new byte[4096], 0).Result;

        // Assert
        Assert.IsInstanceOfType<byte[]>(result);
        Assert.IsTrue(result.Length == 0);
    }
}
