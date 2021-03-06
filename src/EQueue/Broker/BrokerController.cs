﻿using System;
using System.Net.Sockets;
using ECommon.IoC;
using ECommon.Logging;
using ECommon.Remoting;
using ECommon.Socketing;
using EQueue.Broker.Client;
using EQueue.Broker.LongPolling;
using EQueue.Broker.Processors;
using EQueue.Protocols;

namespace EQueue.Broker
{
    public class BrokerController
    {
        private readonly ILogger _logger;
        private readonly IMessageService _messageService;
        private readonly SocketRemotingServer _producerSocketRemotingServer;
        private readonly SocketRemotingServer _consumerSocketRemotingServer;
        private readonly ClientManager _clientManager;
        public SuspendedPullRequestManager SuspendedPullRequestManager { get; private set; }
        public ConsumerManager ConsumerManager { get; private set; }

        public BrokerSetting Setting { get; private set; }

        public BrokerController() : this(null) { }
        public BrokerController(BrokerSetting setting)
        {
            Setting = setting ?? new BrokerSetting();
            SuspendedPullRequestManager = new SuspendedPullRequestManager();
            ConsumerManager = new ConsumerManager();
            _logger = ObjectContainer.Resolve<ILoggerFactory>().Create(GetType().Name);
            _messageService = ObjectContainer.Resolve<IMessageService>();
            _producerSocketRemotingServer = new SocketRemotingServer(Setting.ProducerSocketSetting, new ProducerSocketEventListener(this));
            _consumerSocketRemotingServer = new SocketRemotingServer(Setting.ConsumerSocketSetting, new ConsumerSocketEventListener(this));
            _clientManager = new ClientManager(this);
        }

        public BrokerController Initialize()
        {
            _producerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.SendMessage, new SendMessageRequestHandler(this));
            _producerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.GetTopicQueueCount, new GetTopicQueueCountRequestHandler());
            _consumerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.PullMessage, new PullMessageRequestHandler(this));
            _consumerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.QueryGroupConsumer, new QueryConsumerRequestHandler(this));
            _consumerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.GetTopicQueueCount, new GetTopicQueueCountRequestHandler());
            _consumerSocketRemotingServer.RegisterRequestHandler((int)RequestCode.ConsumerHeartbeat, new ConsumerHeartbeatRequestHandler(this));
            return this;
        }
        public BrokerController Start()
        {
            _producerSocketRemotingServer.Start();
            _consumerSocketRemotingServer.Start();
            _clientManager.Start();
            SuspendedPullRequestManager.Start();
            _logger.InfoFormat("Broker started, producer:[{0}:{1}], consumer:[{2}:{3}]]",
                Setting.ProducerSocketSetting.Address,
                Setting.ProducerSocketSetting.Port,
                Setting.ConsumerSocketSetting.Address,
                Setting.ConsumerSocketSetting.Port);
            return this;
        }
        public BrokerController Shutdown()
        {
            _producerSocketRemotingServer.Shutdown();
            _consumerSocketRemotingServer.Shutdown();
            _clientManager.Shutdown();
            SuspendedPullRequestManager.Shutdown();
            return this;
        }

        class ProducerSocketEventListener : ISocketEventListener
        {
            private readonly ILogger _logger;
            private BrokerController _brokerController;

            public ProducerSocketEventListener(BrokerController brokerController)
            {
                _brokerController = brokerController;
                _logger = ObjectContainer.Resolve<ILoggerFactory>().Create(GetType().Name);
            }

            public void OnNewSocketAccepted(SocketInfo socketInfo)
            {
                _logger.InfoFormat("Accepted new producer, address:{0}", socketInfo.SocketRemotingEndpointAddress);
            }

            public void OnSocketReceiveException(SocketInfo socketInfo, Exception exception)
            {
                var socketException = exception as SocketException;
                if (socketException != null)
                {
                    _logger.InfoFormat("Producer SocketException, address:{0}, errorCode:{1}", socketInfo.SocketRemotingEndpointAddress, socketException.SocketErrorCode);
                }
                else
                {
                    _logger.InfoFormat("Producer Exception, address:{0}, errorMsg:", socketInfo.SocketRemotingEndpointAddress, exception.Message);
                }
            }
        }
        class ConsumerSocketEventListener : ISocketEventListener
        {
            private readonly ILogger _logger;
            private BrokerController _brokerController;

            public ConsumerSocketEventListener(BrokerController brokerController)
            {
                _brokerController = brokerController;
                _logger = ObjectContainer.Resolve<ILoggerFactory>().Create(GetType().Name);
            }

            public void OnNewSocketAccepted(SocketInfo socketInfo)
            {
                _logger.InfoFormat("Accepted new consumer, address:{0}", socketInfo.SocketRemotingEndpointAddress);
            }

            public void OnSocketReceiveException(SocketInfo socketInfo, Exception exception)
            {
                _brokerController.ConsumerManager.RemoveConsumer(socketInfo.SocketRemotingEndpointAddress);
                var socketException = exception as SocketException;
                if (socketException != null)
                {
                    _logger.InfoFormat("Consumer SocketException, address:{0}, errorCode:{1}", socketInfo.SocketRemotingEndpointAddress, socketException.SocketErrorCode);
                }
                else
                {
                    _logger.InfoFormat("Consumer Exception, address:{0}, errorMsg:", socketInfo.SocketRemotingEndpointAddress, exception.Message);
                }
            }
        }
    }
}
