In progress

```C#
//In Process 1
tiesky.com.SharmIpc.Commander sm = null;

if(sm == null)
                sm = new tiesky.com.SharmIpc.Commander("My unique name for interpocess com in the OS scope");
				
//Somewhere else 3 types of calls
//RPC sync with an answer
var res = sm.RpcCall(new byte[512]);	//Returned always Tuple where Item1 indicates that call was ok, optionally can be setup timeout
//RPC call with callback (second parameter must be not null)
var res = sm.RpcCall(new byte[512], (par) => { });		//In this case res.Item1 is also interesting if it's false there will be no answer
//One direction call
sm.Call(new byte[512]);


//In process 2
```
