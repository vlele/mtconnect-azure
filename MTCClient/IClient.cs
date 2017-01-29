using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace MTConnectSharp
{
	public interface IMTConnectClient
	{
        Device[] Devices { get; }
		string AgentUrl { get; set; }
		void Probe();
		void StartStreaming();
	}
}
