﻿using System;
using Microsoft.AspNet.SignalR.Client.Hubs;
using Newtonsoft.Json;
using Rebus.BusHub.Client.Jobs;
using Rebus.BusHub.Messages;
using Rebus.Logging;

namespace Rebus.BusHub.Client
{
    public class BusHubClient : IDisposable
    {
        static ILog log;

        static BusHubClient()
        {
            RebusLoggerFactory.Changed += f => log = f.GetCurrentClassLogger();
        }

        public string InputQueueAddress { get; private set; }

        static readonly JsonSerializerSettings SerializerSettings =
            new JsonSerializerSettings {TypeNameHandling = TypeNameHandling.All};

        readonly HubConnection connection;
        readonly IHubProxy hubProxy;

        readonly Job[] jobs =
            new Job[]
                {
                    new NotifyClientIsOnline(),
                    new SendHeartbeat(), 
                    new NotifyClientIsOffline(), 
                };

        public BusHubClient(string busHubUri, string inputQueueAddress)
        {
            ClientId = Guid.NewGuid();
            InputQueueAddress = inputQueueAddress;

            log.Info("Establishing hub connection to {0}", busHubUri);
            connection = new HubConnection(busHubUri);
            
            log.Info("Creating hub proxy");
            hubProxy = connection.CreateHubProxy("RebusHub");
            hubProxy.On("MessageToClient", (string str) => ReceiveMessage(Deserialize(str)));

            log.Info("Starting connection");
            connection.Start().Wait();
            log.Info("Started!");
        }

        public Guid ClientId { get; private set; }

        public event Action BeforeDispose = delegate { }; 

        public void Initialize(IRebusEvents events)
        {
            log.Info("Starting bus hub client");

            foreach (var job in jobs)
            {
                log.Debug("Initializing job {0}", job);
                job.MessageSent += message => MessageSentByJob(job, message);
                job.Initialize(events, this);
            }
        }

        public void Send(BusHubMessage message)
        {
            log.Debug("Sending bus hub message: {0}", message);
            message.ClientId = ClientId.ToString();
            hubProxy.Invoke("MessageToHub", Serialize(message));
        }

        void MessageSentByJob(Job job, BusHubMessage message)
        {
            Send(message);
        }

        object Deserialize(string str)
        {
            return JsonConvert.DeserializeObject(str, SerializerSettings);
        }

        void ReceiveMessage(object message)
        {
            
        }

        string Serialize(BusHubMessage message)
        {
            var str = JsonConvert.SerializeObject(message, SerializerSettings);

            return str;
        }

        public void Dispose()
        {
            BeforeDispose();

            foreach (var job in jobs)
            {
                var disposable = job as IDisposable;
                if (disposable == null) continue;

                log.Debug("Disposing job {0}", job);
                disposable.Dispose();
            }

            log.Info("Disposing connection to bus hub");
            connection.Stop();
        }
    }
}
