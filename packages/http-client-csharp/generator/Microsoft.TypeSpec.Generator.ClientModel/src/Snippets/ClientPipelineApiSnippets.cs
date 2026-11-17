// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.ClientModel.Providers;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.ClientModel.Snippets
{
    internal static class ClientPipelineApiSnippets
    {
        private const string _processMessageAsync = "ProcessMessageAsync";
        private const string _processMessage = "ProcessMessage";

        public static HttpResponseApi ProcessMessage(this ClientPipelineApi pipeline, ValueExpression message, HttpRequestOptionsApi? requestOptions, bool isAsync)
            => InvokeWithWitness(pipeline, isAsync ? _processMessageAsync : _processMessage, message, requestOptions ?? Null, isAsync).ToApi<HttpResponseApi>();

        public static ClientResponseApi ProcessHeadAsBoolMessage(this ClientPipelineApi pipeline, ValueExpression message, HttpRequestOptionsApi requestOptions, bool isAsync)
            => InvokeWithWitness(pipeline, isAsync ? "ProcessHeadAsBoolMessageAsync" : "ProcessHeadAsBoolMessage", message, requestOptions, isAsync).ToApi<ClientResponseApi>();

        private static InvokeMethodExpression InvokeWithWitness(ClientPipelineApi pipeline, string methodName, ValueExpression message, ValueExpression options, bool isAsync)
        {
            ValueExpression[] ownedArguments = [message, options];
            var invocation = pipeline.Invoke(methodName, new ProducerOwnedReadOnlyList<ValueExpression>(ownedArguments), isAsync);
            if (ConstructedArgumentBridge.Enabled(invocation))
            {
                ConstructedArgumentBridge.Record(invocation, ownedArguments);
                ConstructedArgumentBridge.RegisterWrapperType(invocation, typeof(PipelineResponseProvider));
                ConstructedArgumentBridge.RegisterWrapperType(invocation, typeof(ClientPipelineProvider));
                ConstructedArgumentBridge.RegisterWrapperType(invocation, typeof(RequestOptionsProvider));
            }
            return invocation;
        }
    }
}
