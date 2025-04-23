**SharmIPC .NET**
=====================
![Image of Build](https://img.shields.io/badge/SharmIPC.NET-stable%20v1.21-4BA2AD.svg) 
![Image of Build](https://img.shields.io/badge/License-BSD%203,%20FOSS-FC0574.svg) 
![Image of Build](https://img.shields.io/badge/Roadmap-completed-33CC33.svg)
[![NuGet Badge](https://img.shields.io/nuget/dt/SharmIPC?color=blue&label=Nuget%20downloads)](https://www.nuget.org/packages/SharmIPC/)
[![Image of Build](https://img.shields.io/badge/Powered%20by-tiesky.com-1883F5.svg)](http://tiesky.com)

Inter-process communication (IPC engine) between 2 partner processes of one OS:
<br>- .NET Framework 4.5 > /.NETCore 2.0 / .NETStandard 2.0 / .NET 6> . Based on memory-mapped files and on NamedPipes
<br>- Written on C#
<br>- Fast and lightweight
<br>- Sync and Async calls with timeouts
<br>- a la RPC
<br>- a la send and forget
- <a href = 'https://www.nuget.org/packages/SharmIPC/'  target='_blank'>Grab it from NuGet</a>
- <a href = 'https://github.com/hhblaze/SharmIPC/tree/master/Process1/SharmIpc/bin/Release/'  target='_blank'>Assemblies</a>

=====================
*Usage example:* 
```C#
// Added SharmNpc which works under .NET 6+ on Linux (Named MemoryMappedFiles are not supported there).
// Both versions are included and usable. SharmNpc is based on Named Pipes and serves as a complete drop-in replacement.
// With SharmNpc, one process becomes the server and another becomes the client.
// Previously, we used:
tiesky.com.SharmIpc sm = null;
// Now we can change to:
tiesky.com.ISharm sm = null;
// and use either SharmIpc (Windows) or SharmNpc (Windows/Linux).

// Server listener
sm = new tiesky.com.SharmNpc("MNPC", tiesky.com.SharmNpcInternals.PipeRole.Server, this.RemoteCall);
// Client connector
sm = new tiesky.com.SharmNpc("MNPC", tiesky.com.SharmNpcInternals.PipeRole.Client, this.AsyncRemoteCallHandler);
//or
sm = new tiesky.com.SharmNpc("MNPC", tiesky.com.SharmNpcInternals.PipeRole.Server,
    this.AsyncRemoteCallHandler,
    50000, 100000000,
    (descr, excep) =>
    {
       
    })
	{
		 Verbose = false,
		 PeerDisconnected = () => {
			 
		 },
		 PeerConnected = () =>
		 {
			 
		 }
	};
// The rest remains the same - it's a drop-in replacement.
// Note: Currently there's a connection timeout (if server or client waits longer than 30 seconds for connection establishment, it aborts).
// Also, after establishing communication, if one peer disconnects, the other doesn't automatically restore listening/connecting behavior.
// These behaviors can be customized, but represent different implementation strategies.
// In this initial SharmNpc version, we don't provide configuration flexibility for these scenarios - this may be added in future updates or by request.

//Check Process1 and Process2 folder for the examples.
```

```C#
//Process 1 and Process2

tiesky.com.SharmIpc sm = null;

void Init()
{
	if(sm == null)
	  	sm = new tiesky.com.SharmIpc(
		  	"My unique name for interpocess com in the OS scope, must be the same for both processes"
		  	, this.RemoteCall
			
			//unique name must start from the prefix "Global/" to make communication available 
			//	for processes executed by different OS users
			
		  	//there are also extra parameters in constructor with description:
		  	//optional send buffer capacity, must be the same for both partners
		  	//optional total not send messages buffer capacity
			
	  	);
  	
  
  	
  	
}

void Dispose()
{
	//!!!For the graceful termination, in the end of your program, 
  	//SharmIpc instance must be disposed
  	if(sm != null){
  		sm.Dispose();
  		sm=null;
  	}
}

Tuple<bool,byte[]> RemoteCall(byte[] data)
{
		//This will be called async when remote partner makes any request
		
		//This is a response to remote partner
		return new Tuple<bool,byte[]>(true,new byte[] {1,2,3,4});	
}

void MakeRemoteRequestWithResponse()
{
	 //Making remote request (a la RPC). SYNC
	 Tuple<bool,byte[]> res = sm.RemoteRequest(new byte[512]);
	 //or with callback
	 //var res = sm.RemoteRequest(data, (par) => { },30000);
	 
	 //if !res.Item1 then our call was not put to the sending buffer, 
	 //due to its threshold limitation
	 //or remote partner answered with technical mistake
	 //or timeout encountered
	 if(res.Item1)
	 {
	 		//Analyzing response res.Item2
	 }
}

// async/await pattern
async void MakeRemoteRequestWithResponse()
{
	 //Making remote request (a la RPC). SYNC
	 //Non-blocking current thread construction!
	 Tuple<bool,byte[]> res = await sm.RemoteRequestAsync(new byte[512]);
	 //or with callback way
	 //var res = await sm.RemoteRequestAsync(data, (par) => { },30000);
	 
	 //if !res.Item1 then our call was not put to the sending buffer, 
	 //due to its threshold limitation
	 //or remote partner answered with technical mistake
	 //or timeout encountered
	 if(res.Item1)
	 {
	 		//Analyzing response res.Item2
	 }
}

void MakeRemoteRequestWithoutResponse()
{
	 //Making remote request (a la send and forget)
	 Tuple<bool,byte[]> res = sm.RemoteRequestWithoutResponse(new byte[512]);
	 
	 if(!res.Item1)
	 {
	 	//Our request was not cached for sending, we can do something
	 }
}

//----starting from v1.04 it's possible to answer on remote call in async way:

//New way of instantiation of SharmIPC 
//sm = new tiesky.com.SharmIpc("MyUniqueNameForBothProcesses",this.AsyncRemoteCallHandler);

//where AsyncRemoteCallHandler will be used instead of RemoteCall and gives an ability to answer to 
//the remote partner's request in async way

void AsyncRemoteCallHandler(ulong msgId, byte[] data)
{
        //msgId must be returned back
        //data is received from remote partner
        
        //answer to remote partner:
        sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 5 }));
         
       
}


```
=====================
*Benchmarks:*
```txt
[DLL size is 65 KB] (SharmNpc works 20-30% faster than SharmIpc)

[512 bytes package in both directions]
Remote async and sync calls with response (a la RPC), full-duplex, with the speed of 20 MB/s.
Remote async calls without response (a la send and forget), full-duplex, with the speed of 80 MB/s.

[10000 bytes package in both directions]
Remote async and sync calls with response (a la RPC), full-duplex, with the speed of 320 MB/s.
Remote async calls without response (a la send and forget), full-duplex, with the speed of 700 MB/s.

[1 byte package in both directions]
Remote async and sync calls with response (a la RPC), full-duplex, with the speed of 40000 call/s.
Remote async calls without response (a la send and forget), full-duplex, with the speed of 120000 calls/s.

and if you need more speed, just add in both processes more SharmIPC instances
```

<a href = 'https://github.com/hhblaze/DBreeze'  target='_blank'>---- Check also our DBreeze database for .NET ----</a>

hhblaze@gmail.com
