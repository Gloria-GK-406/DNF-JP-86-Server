namespace Dfo.Server.Data.Networking;

public static class A21UdpDiscoveryProtocol
{
    public const int PortA = 12311;

    public const int PortB = 12312;

    public const byte BindingRequest = 0x01;

    public const byte ChangePortRequest = 0x05;

    public const byte KeepaliveRequest = 0x00;

    public const byte BindingResponse = 0x02;

    public const int ResponseLength = 7;
}
