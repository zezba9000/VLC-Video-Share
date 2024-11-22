using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace Reign
{
	public class OpenedNATDevice
	{
		public readonly NatDevice device;
		public readonly IPAddress localAddress, externalAddress;
		public readonly IPEndPoint hostEndPoint;
		public readonly int port;
		public readonly bool openTCP, openUDP;

		public OpenedNATDevice(NatDevice device, IPAddress localAddress, IPAddress externalAddress, IPEndPoint hostEndPoint, bool openTCP, bool openUDP, int port)
		{
			this.device = device;
			this.localAddress = localAddress;
			this.externalAddress = externalAddress;
			this.hostEndPoint = hostEndPoint;
			this.port = port;
			this.openTCP = openTCP;
			this.openUDP = openUDP;
		}
	}

	public static class NATUtils
	{
		public static async Task<OpenedNATDevice> OpenPortAsync(bool openTCP, bool openUDP, int port, int lifetimeInSeconds, string desc)
		{
			if (!openTCP && !openUDP) throw new Exception("At least one protocol must be open");

			// find first supported NAT device
			var nat = new NatDiscoverer();
			var cts = new CancellationTokenSource(5000);
			var openNATDevice = await nat.DiscoverDeviceAsync(PortMapper.Upnp, cts);
			if (openNATDevice == null) return null;

			// open port on device
			if (openTCP) await openNATDevice.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, lifetimeInSeconds, desc));
			if (openUDP) await openNATDevice.CreatePortMapAsync(new Mapping(Protocol.Udp, port, port, lifetimeInSeconds, desc));

			// return result
			var externalAddress = await openNATDevice.GetExternalIPAsync();
			return new OpenedNATDevice(openNATDevice, openNATDevice.LocalAddress, externalAddress, openNATDevice.HostEndPoint, openTCP, openUDP, port);
		}

		public static OpenedNATDevice OpenPort(bool openTCP, bool openUDP, int port, int lifetimeInSeconds, string desc)
		{
			var task = OpenPortAsync(openTCP, openUDP, port, lifetimeInSeconds, desc);
			task.Wait();
			return task.Result;
		}

		public static async Task ClosePortAsync(OpenedNATDevice device)
		{
			if (device.openTCP) await device.device.DeletePortMapAsync(new Mapping(Protocol.Tcp, device.port, device.port));
			if (device.openUDP) await device.device.DeletePortMapAsync(new Mapping(Protocol.Udp, device.port, device.port));
		}

		public static void ClosePort(OpenedNATDevice device)
		{
			var task = ClosePortAsync(device);
			task.Wait();
		}
	}
}
