﻿using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Elasticsearch.Net;
using FluentAssertions;
using Tests.Framework;

namespace Tests.ClientConcepts.ConnectionPooling.Exceptions
{
	public class UnexpectedExceptions
    {
		/**=== Unexpected exceptions 
        * 
		* When a client call throws an exception that the `IConnection` cannot handle, the exception will bubble
		* out of the client as an UnexpectedElasticsearchClientException, regardless whether the client is configured to 
        * throw or not.
        * 
		* An `IConnection` is in charge of knowing what exceptions it can recover from or not, and the default `IConnection` 
        * that is based on `WebRequest` can and will recover from `WebExceptions` but others will be grounds for 
        * immediately exiting the pipeline.
		*/

		[U] public async Task UnexpectedExceptionsBubbleOut()
		{
			var audit = new Auditor(() => Framework.Cluster
				.Nodes(10)
				.ClientCalls(r => r.SucceedAlways())
				.ClientCalls(r => r.OnPort(9201).FailAlways(new Exception("boom!")))
				.StaticConnectionPool()
				.Settings(s => s.DisablePing())
			);

			audit = await audit.TraceCall(
				new ClientCall {
					{ AuditEvent.HealthyResponse, 9200 },
				}
			);

			audit = await audit.TraceUnexpectedException(
				new ClientCall {
					{ AuditEvent.BadResponse, 9201 },
				},
				(e) =>
				{
					e.FailureReason.Should().Be(PipelineFailure.Unexpected);
					e.InnerException.Should().NotBeNull();
					e.InnerException.Message.Should().Be("boom!");
				}
			);
		}

		/**
		* Sometimes an unexpected exception happens further down in the pipeline, this is why we 
		* wrap them inside an UnexpectedElasticsearchClientException so that information about where 
		* in the pipeline the unexpected exception is not lost, here a call to 9200 fails using a webexception.
		* It then falls over to 9201 which throws an hard exception from within IConnection. We assert that we 
		* can still see the audit trail for the whole coordinated request.
		*/

		[U] public async Task WillFailOverKnowConnectionExceptionButNotUnexpected()
		{
			var audit = new Auditor(() => Framework.Cluster
				.Nodes(10)
#if DOTNETCORE
				.ClientCalls(r => r.OnPort(9200).FailAlways(new System.Net.Http.HttpRequestException("recover")))
#else
				.ClientCalls(r => r.OnPort(9200).FailAlways(new WebException("recover")))
#endif 
				.ClientCalls(r => r.OnPort(9201).FailAlways(new Exception("boom!")))
				.StaticConnectionPool()
				.Settings(s => s.DisablePing())
			);

			audit = await audit.TraceUnexpectedException(
				new ClientCall {
					{ AuditEvent.BadResponse, 9200 },
					{ AuditEvent.BadResponse, 9201 },
				},
				(e) =>
				{
					e.FailureReason.Should().Be(PipelineFailure.Unexpected);
					e.InnerException.Should().NotBeNull();
					e.InnerException.Message.Should().Be("boom!");
				}
			);
		}

		/**
		* An unexpected hard exception on ping and sniff is something we *do* try to recover from and failover.
		* Here, pinging nodes on first use is enabled and the node on port 9200 throws on ping; in this scenario, 
        * we still fallover to node on port 9201, whose ping succeeds.
		* Following this, the client call on 9201 throws a hard exception that we cannot recover from
		*/

		[U] public async Task PingUnexceptedExceptionDoesFailOver()
		{
			var audit = new Auditor(() => Framework.Cluster
				.Nodes(10)
				.Ping(r => r.OnPort(9200).FailAlways(new Exception("ping exception")))
				.Ping(r => r.OnPort(9201).SucceedAlways())
				.ClientCalls(r => r.OnPort(9201).FailAlways(new Exception("boom!")))
				.StaticConnectionPool()
				.AllDefaults()
			);

			audit = await audit.TraceUnexpectedException(
				new ClientCall {
					{ AuditEvent.PingFailure, 9200 },
					{ AuditEvent.PingSuccess, 9201 },
					{ AuditEvent.BadResponse, 9201 },
				},
				(e) =>
				{
					e.FailureReason.Should().Be(PipelineFailure.Unexpected);

					/** InnerException is the exception that brought the request down */
					e.InnerException.Should().NotBeNull();
					e.InnerException.Message.Should().Be("boom!");

					/** The hard exception that happened on ping is still available though */
					e.SeenExceptions.Should().NotBeEmpty();
					var pipelineException = e.SeenExceptions.First();
					pipelineException.FailureReason.Should().Be(PipelineFailure.PingFailure);
					pipelineException.InnerException.Message.Should().Be("ping exception");

					/** Seen exception is hard to relate back to a point in time, the exception is also 
					* available on the audit trail
					*/
					var pingException = e.AuditTrail.First(a => a.Event == AuditEvent.PingFailure).Exception;
					pingException.Should().NotBeNull();
					pingException.Message.Should().Be("ping exception");
				}
			);
		}
	}
}
