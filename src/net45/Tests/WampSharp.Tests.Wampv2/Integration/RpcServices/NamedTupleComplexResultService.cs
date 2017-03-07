#if !NET40
using System;
using System.Runtime.CompilerServices;
using WampSharp.V2.Rpc;

namespace WampSharp.Tests.Wampv2.Integration.RpcServices
{
    public class NamedTupleComplexResultService
    {
        public (int c, int ci) AddComplex(int a, int ai, int b, int bi)
        {
            return (a + b, ai + bi);
        }
    }
}
#endif