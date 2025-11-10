using NUnit.Framework;
using NetSdrClientApp;
using System;

// Створіть цей клас у вашому основному проєкті, якщо його немає, 
// щоб тести мали що покривати! (Це має бути зроблено окремим комітом 
// або файлом у папці NetSdrClientApp)
public class ConnectionManager
{
    public bool IsConnectionStringValid(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.ToUpper().Contains("IP=") && connectionString.ToUpper().Contains("PORT=");
    }

    public string Connect(string address, int port)
    {
        if (port < 1024)
        {
            throw new ArgumentException("Port is reserved.");
        }
        if (string.IsNullOrWhiteSpace(address))
        {
            return "Address required";
        }
        return $"Connected to {address}:{port}";
    }
}

[TestFixture]
public class ConnectionManagerTests
{
    [Test]
    public void IsConnectionStringValid_GivenCompleteString_ReturnsTrue()
    {
        var manager = new ConnectionManager();
        string validString = "IP=192.168.1.100;Port=8080;Timeout=1000";
        bool actual = manager.IsConnectionStringValid(validString);
        Assert.That(actual, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("IP=123")]
    public void IsConnectionStringValid_GivenInvalidOrMissingData_ReturnsFalse(string invalidString)
    {
        var manager = new ConnectionManager();
        bool actual = manager.IsConnectionStringValid(invalidString);
        Assert.That(actual, Is.False);
    }

    [Test]
    public void Connect_GivenReservedPort_ThrowsArgumentException()
    {
        var manager = new ConnectionManager();
        Assert.Throws<ArgumentException>(() => manager.Connect("localhost", 22));
    }

    [Test]
    public void Connect_GivenValidParameters_ReturnsSuccessfulConnectionMessage()
    {
        var manager = new ConnectionManager();
        string expectedAddress = "10.0.0.5";
        int expectedPort = 5000;
        string expectedMessage = $"Connected to {expectedAddress}:{expectedPort}";
        string actual = manager.Connect(expectedAddress, expectedPort);
        Assert.That(actual, Is.EqualTo(expectedMessage));
    }
}
