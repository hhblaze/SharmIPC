**SharmIPC .NET**
=====================
![Image of Build](https://img.shields.io/badge/SharmIPC .NET-stable%20v01.03-4BA2AD.svg) 
![Image of Build](https://img.shields.io/badge/License-BSD%203,%20FOSS-FC0574.svg) 
![Image of Build](https://img.shields.io/badge/Roadmap-completed-33CC33.svg)
![Image of Build](https://img.shields.io/badge/Powered%20by-tiesky.com-1883F5.svg)

Inter-process communication (IPC engine) between 2 partner processes of one OS:
<br>- .NET 4.5 / MONO, based on memory-mapped files
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
		  	//optional send buffer capacity, must be the same for both partners
		  	//optional total not send messages buffer capacity
	  	);
  	
  	//there are also extra parameters in constructor with description
  	
  	//!!!For the graceful termination, in the end of your program, 
  	//SharmIpc instance must be disposed
  	//sm.Dispose();
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

//----starting from v1.03 it's possible to answer on remote call in async way:

//After standard instantiation of SharmIPC 
//sm = new tiesky.com.SharmIpc("MyUniqueNameForBothProcesses",this.RemoteCall);
//it's necessary to add:

sm.AsyncRemoteCallHandler = this.AsyncRemoteCallHandler;

//where AsyncRemoteCallHandler will be used instead of RemoteCall

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
```

hhblaze@gmail.com
