**SharmIPC .NET**
=====================
![Image of Build](https://img.shields.io/badge/SharmIPC .NET-stable%20version%201.001-4BA2AD.svg)

Inter-process communication (IPC engine) for .NET / MONO, based on memory-mapped files
<br>Written on C#
<br>Fast and light weight
- <a href = 'https://www.nuget.org/packages/SharmIPC/'  target='_blank'>Grab it from NuGet</a>

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
	  	);
  	
  	//there are also extra parameters in constructor with description
}

Tuple<bool,byte[]> RemoteCall(byte[] data)
{
		//This will be called async when remote partner makes any request
		
		//This is a response to remote partner
		return new Tuple<bool,byte[]>(true,new byte[] {1,2,3,4});	
}

void MakeRemoteRequestWithResponse()
{
	 //Making remote request (RPC). SYNC
	 Tuple<bool,byte[]> res = sm.RemoteRequest(new byte[512]);
	 //or async way
	 //var res = sm.RemoteRequest(data, (par) => { },30000);
	 
	 //if !res.Item1 then our call was not put to the sending buffer, due to its threshold limitation
	 //or remote partner answered with technical mistake
	 //or timeout encountered
	 if(res.Item1)
	 {
	 		//Analyzing response res.Item2
	 }
}

void MakeRemoteRequestWithoutResponse()
{
	 //Making remote request (RPC)
	 Tuple<bool,byte[]> res = sm.RemoteRequest(new byte[512]);
	 
	 if(!res.Item1)
	 {
	 		//Our request was not cached for sending, we can do something
	 }
}

```
=====================
*Benchmarks:*
```txt
[512 bytes package in both directions]
Remote async and sync calls with response, full-duplex, with the speed of 20MB/s.
Remote async calls without response, full-duplex, with the speed of 80MB/s.
```
