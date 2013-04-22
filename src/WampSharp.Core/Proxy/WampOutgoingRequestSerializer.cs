﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WampSharp.Core.Contracts;
using WampSharp.Core.Message;
using WampSharp.Core.Serialization;

namespace WampSharp.Core.Proxy
{
    public class WampOutgoingRequestSerializer<TMessage> : IWampOutgoingRequestSerializer<WampMessage<TMessage>>
    {
        private readonly IWampFormatter<TMessage> mFormatter;

        public WampOutgoingRequestSerializer(IWampFormatter<TMessage> formatter)
        {
            mFormatter = formatter;
        }

        public WampMessage<TMessage> SerializeRequest(MethodInfo method, object[] arguments)
        {
            WampHandlerAttribute attribute = 
                method.GetCustomAttribute<WampHandlerAttribute>();

            WampMessageType messageType = attribute.MessageType;

            WampMessage<TMessage> result = new WampMessage<TMessage>()
                                               {
                                                   MessageType = messageType
                                               };

            var parameters = method.GetParameters()
                                   .Zip(arguments,
                                        (parameterInfo, argument) =>
                                        new
                                            {
                                                parameterInfo,
                                                argument
                                            })
                                   .Where(x => x.parameterInfo.ParameterType != typeof (IWampClient));

            List<TMessage> messageArguments = new List<TMessage>();

            foreach (var parameter in parameters)
            {
                if (!parameter.parameterInfo.IsDefined(typeof (ParamArrayAttribute)))
                {
                    TMessage serialized = mFormatter.Serialize(parameter.argument);
                    messageArguments.Add(serialized);
                }
                else
                {
                    object[] paramsArray = parameter.argument as object[];

                    foreach (object param in paramsArray)
                    {
                        TMessage serialized = mFormatter.Serialize(param);
                        messageArguments.Add(serialized);
                    }
                }
            }

            result.Arguments = messageArguments.ToArray();

            return result;
        }
    }
}