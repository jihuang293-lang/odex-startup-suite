using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace CodexLocalStartup {
 public sealed class TcpListenerEvidence {
  public string LocalAddress { get; set; }
  public int LocalPort { get; set; }
  public uint OwningProcess { get; set; }
  public string State { get { return "Listen"; } }
 }
 // Read the same Windows owner-PID listener tables used for endpoint
 // verification, without initializing the much slower NetTCPIP CIM provider.
 public static class StartupTcpTables {
  [DllImport("iphlpapi.dll",SetLastError=false)]
  static extern uint GetExtendedTcpTable(IntPtr table,ref uint size,bool ordered,int family,int tableClass,uint reserved);
  static byte[] Bytes(IntPtr table,int offset,int count) {
   byte[] bytes=new byte[count];Marshal.Copy(IntPtr.Add(table,offset),bytes,0,count);return bytes;
  }
  static int Port(uint value) { return (int)(((value&255)<<8)|((value>>8)&255)); }
  static void ReadFamily(int family,int wanted,List<TcpListenerEvidence> output) {
   uint size=0;
   uint result=GetExtendedTcpTable(IntPtr.Zero,ref size,false,family,3,0); // TCP_TABLE_OWNER_PID_LISTENER
   if(result!=0&&result!=122)throw new Win32Exception((int)result);
   for(int attempt=0;attempt<4;attempt++) {
    if(size<4||size>8*1024*1024)throw new InvalidOperationException("Unexpected TCP table size.");
    uint allocated=size;IntPtr table=Marshal.AllocHGlobal((int)allocated);
    try {
     result=GetExtendedTcpTable(table,ref size,false,family,3,0);
     if(result==122)continue; // A listener changed between the size/read calls.
     if(result!=0)throw new Win32Exception((int)result);
     if(size>allocated||size<4)throw new InvalidOperationException("TCP table changed unexpectedly.");
     uint count=unchecked((uint)Marshal.ReadInt32(table));int stride=family==2?24:56;
     if((ulong)count>(allocated-4)/(uint)stride)throw new InvalidOperationException("Invalid TCP listener row count.");
     for(int index=0;index<(int)count;index++) {
      int row=4+index*stride;
      uint state=unchecked((uint)Marshal.ReadInt32(table,row+(family==2?0:48)));
      int port=Port(unchecked((uint)Marshal.ReadInt32(table,row+(family==2?8:20))));
      if(state!=2||port!=wanted)continue;
      IPAddress address=family==2?new IPAddress(Bytes(table,row+4,4)):
       new IPAddress(Bytes(table,row,16),unchecked((uint)Marshal.ReadInt32(table,row+16)));
      output.Add(new TcpListenerEvidence {LocalAddress=address.ToString(),LocalPort=port,
       OwningProcess=unchecked((uint)Marshal.ReadInt32(table,row+(family==2?20:52)))});
     }
     return;
    } finally {Marshal.FreeHGlobal(table);}
   }
   throw new InvalidOperationException("TCP listener table remained unstable.");
  }
  public static TcpListenerEvidence[] ReadListeners(int port) {
   if(port<1||port>65535)throw new ArgumentOutOfRangeException("port");
   var result=new List<TcpListenerEvidence>();
   // Both families must succeed. Partial evidence must not omit an external
   // or foreign listener; callers fall back to the original provider on error.
   ReadFamily(2,port,result);ReadFamily(23,port,result);
   return result.ToArray();
  }
 }
}
