**SharmIPC .NET**
=====================
![Image of Build](https://img.shields.io/badge/SharmIPC.NET-stable%20v1.10-4BA2AD.svg) 
![Image of Build](https://img.shields.io/badge/License-BSD%203,%20FOSS-FC0574.svg) 
![Image of Build](https://img.shields.io/badge/Roadmap-completed-33CC33.svg)
![Image of Build](https://img.shields.io/badge/Powered%20by-tiesky.com-1883F5.svg)

Inter-process communication (IPC engine) between 2 partner processes of one OS:
<br>- .NET 4.5 / MONO /.NETCore / UWP. Based on memory-mapped files
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
	 //or async way
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

void MakeRemoteRequestWithoutResponse()
{
	 //Making remote request (a la send and forget)
	 Tuple<bool,byte[]> res = sm.RemoteRequest(new byte[512]);
	 
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
        /* 
       	//or in such way
            Task.Run(() =>
                {
                    sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 5 }));
                });
        */
}


```
=====================
*Benchmarks:*
```txt
[DLL size is 15 KB]

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
