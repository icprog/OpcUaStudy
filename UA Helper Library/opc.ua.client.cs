﻿using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Hsl
{
    public class OpcUaClient
    {

        #region 私有对象

        private ApplicationInstance application = new ApplicationInstance();

        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private bool m_connectedOnce;
        private Subscription m_subscription;
        private MonitoredItemNotificationEventHandler m_MonitoredItem_Notification;
        private int m_reconnectPeriod = 10;
        private string m_ServerUrl = string.Empty;
        /// <summary>
        /// 会话重连时的处理
        /// </summary>
        private SessionReconnectHandler m_reconnectHandler;
        private EventHandler m_ReconnectComplete;
        private EventHandler m_ReconnectStarting;
        private EventHandler m_KeepAliveComplete;
        private EventHandler m_ConnectComplete;
        private EventHandler<OpcStausEventArgs> m_OpcStatusChange;

        private CertificateValidationEventHandler m_CertificateValidation;

        #endregion


        #region 构造方法
        public OpcUaClient(bool isConfigLoadByFile)
        {
            if (isConfigLoadByFile)
            {
                // 加载应用程序的配置
                application.LoadApplicationConfiguration(false);
            }
            else
            {
                application.ApplicationConfiguration = GetDefaultApplicationConfig();
            }
            // 检查应用程序的证书
            application.CheckApplicationInstanceCertificate(false, 0);

            m_configuration = application.ApplicationConfiguration;
            Initilization();
        }

        public OpcUaClient(ApplicationConfiguration appConfig)
        {
            // 加载应用程序的配置
            application.ApplicationConfiguration = appConfig;
            // 检查应用程序的证书
            application.CheckApplicationInstanceCertificate(false, 0);

            m_configuration = application.ApplicationConfiguration;
            Initilization();
        }


        private void Initilization()
        {
            application.ConfigSectionName = "";
            application.ApplicationType = application.ApplicationConfiguration.ApplicationType;
            application.ApplicationName = application.ApplicationConfiguration.ApplicationName;
            m_CertificateValidation = new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            
        }

        #endregion

        #region 公开的属性

        /// <summary>
        /// 服务器的连接地址
        /// </summary>
        public string ServerUrl
        {
            get => m_ServerUrl;
            set => m_ServerUrl = value;
        }

        /// <summary>
        /// 当前活动的会话
        /// </summary>
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// 获取或设置一个标志，指示连接时是否应该忽略域检查
        /// </summary>
        public bool DisableDomainCheck { get; set; }
        /// <summary>
        /// 在连接服务器的时候是否使用安全设置
        /// </summary>
        public bool UseSecurity { get; set; } = false;

        /// <summary>
        /// 创建会话时要使用的用户身份
        /// </summary>
        public IUserIdentity UserIdentity { get; set; }

        /// <summary>
        /// 要创建的会话的名称
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        /// The locales to use when creating the session.
        /// 创建会话时使用的区域设置。
        /// </summary>
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// 重新连接尝试之间的秒数（0表示重新连接被禁用）。
        /// </summary>
        [DefaultValue(10)]
        public int ReconnectPeriod
        {
            get { return m_reconnectPeriod; }
            set { m_reconnectPeriod = value; }
        }

        /// <summary>
        /// Raised when a good keep alive from the server arrives.
        /// 当一个来自服务器的正确的活动状态到达时触发
        /// </summary>
        public event EventHandler KeepAliveComplete
        {
            add { m_KeepAliveComplete += value; }
            remove { m_KeepAliveComplete -= value; }
        }

        /// <summary>
        /// 当重连服务器操作启动的时候触发
        /// </summary>
        public event EventHandler ReconnectStarting
        {
            add { m_ReconnectStarting += value; }
            remove { m_ReconnectStarting -= value; }
        }

        /// <summary>
        /// 当重连操作完成的时候触发
        /// </summary>
        public event EventHandler ReconnectComplete
        {
            add { m_ReconnectComplete += value; }
            remove { m_ReconnectComplete -= value; }
        }

        /// <summary>
        /// 当连接服务器完成时触发，无论是成功还是失败
        /// </summary>
        public event EventHandler ConnectComplete
        {
            add { m_ConnectComplete += value; }
            remove { m_ConnectComplete -= value; }
        }
        /// <summary>
        /// 当客户端的状态发生变化时触发的事件
        /// </summary>

        public event EventHandler<OpcStausEventArgs> OpcStatusChange
        {
            add { m_OpcStatusChange += value; }
            remove { m_OpcStatusChange -= value; }
        }

        #endregion

        #region 公开的方法
        
        /// <summary>
        /// 连接到服务器
        /// </summary>
        public void Connect()
        {
            string serverUrl = m_ServerUrl;

            if (string.IsNullOrEmpty(serverUrl)) throw new Exception("服务器的地址为空！");

            // 先把当前的会话断开
            Disconnect();
            

            if (m_configuration == null)
            {
                throw new ArgumentNullException("m_configuration");
            }

            // 根据当前的设置，选择一个最好的节点
            EndpointDescription endpointDescription = SelectEndpoint(serverUrl, UseSecurity);

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            m_session = Session.Create(
                m_configuration,
                endpoint,
                false,
                !DisableDomainCheck,
                (String.IsNullOrEmpty(SessionName)) ? m_configuration.ApplicationName : SessionName,
                60000,
                UserIdentity,
                PreferredLocales);

            // set up keep alive callback.
            m_session.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);

            // 触发一个事件
            DoConnectComplete(null);
            
        }


        /// <summary>
        /// 使用指定的参数连接到服务器
        /// </summary>
        /// <param name="serverUrl"></param>
        /// <param name="useSecurity"></param>
        public void Connect(string serverUrl, bool useSecurity)
        {
            ServerUrl = serverUrl;
            UseSecurity = useSecurity;
            Connect();
        }


        /// <summary>
        /// 断开和服务器的连接
        /// </summary>
        public void Disconnect()
        {
            //更新状态控件的信息
            UpdateStatus(false, DateTime.UtcNow, "Disconnected");

            // stop any reconnect operation.
            // 停止任何的重连操作
            if (m_reconnectHandler != null)
            {
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
            }

            // disconnect any existing session.
            // 断开任何的存在的会话
            if (m_session != null)
            {
                m_session.Close(10000);
                m_session = null;
            }

            // 引发一个事件
            DoConnectComplete(null);
        }

        /// <summary>
        /// 提示用户选择本地主机上的服务器。
        /// </summary>
        public void Discover()
        {
            string endpointUrl = new DiscoverServerDlg().ShowDialog(m_configuration, null);

            if (endpointUrl != null)
            {
                ServerUrl = endpointUrl;
            }
        }

        /// <summary>
        /// 在系统连接之前调用，配置为日志输出
        /// </summary>
        public void SetLogOutPut()
        {
            m_configuration.TraceConfiguration.OutputFilePath = @"Logs\OpcUaHslClientLog.txt";
            m_configuration.TraceConfiguration.DeleteOnLoad = true;
        }
        

        #endregion

        #region 私有的方法
        private ApplicationConfiguration GetDefaultApplicationConfig()
        {
            ApplicationConfiguration config = new ApplicationConfiguration()
            {
                ApplicationName = "OpcUaHslClient",
                ApplicationType = ApplicationType.Client
            };
            SecurityConfiguration sConfig = new SecurityConfiguration();
            sConfig.ApplicationCertificate = new CertificateIdentifier()
            {
                StoreType = "Directory",
                StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault",
                SubjectName = config.ApplicationName,
            };

            sConfig.TrustedPeerCertificates = new CertificateTrustList()
            {
                StoreType = "Directory",
                StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications",
            };

            sConfig.TrustedIssuerCertificates = new CertificateTrustList()
            {
                StoreType = "Directory",
                StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities",
            };

            sConfig.RejectedCertificateStore = new CertificateStoreIdentifier()
            {
                StoreType = "Directory",
                StorePath = @"% CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates"
            };

            config.SecurityConfiguration = sConfig;



            config.TransportConfigurations = new TransportConfigurationCollection();
            config.TransportQuotas = new TransportQuotas();
            config.ClientConfiguration = new ClientConfiguration();
            config.TraceConfiguration = new TraceConfiguration()
            {
                OutputFilePath = null,
                DeleteOnLoad = false,
                TraceMasks = 515,
            };

            config.CertificateValidator = new CertificateValidator();
            config.CertificateValidator.Update(config);
            return config;
        }

        /// <summary>
        /// Handles a keep alive event from a session.
        /// 处理会话中维持状态的事件
        /// </summary>
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            try
            {
                // 检查会话是否已经被丢弃
                if (!ReferenceEquals(session, m_session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                // 当通信出错的时候进行重连
                if (ServiceResult.IsBad(e.Status))
                {
                    if (m_reconnectPeriod <= 0)
                    {
                        UpdateStatus(true, e.CurrentTime, "Communication Error ({0})", e.Status);
                        return;
                    }

                    UpdateStatus(true, e.CurrentTime, "Reconnecting in {0}s", m_reconnectPeriod);

                    if (m_reconnectHandler == null)
                    {
                        m_ReconnectStarting?.Invoke(this, e);

                        m_reconnectHandler = new SessionReconnectHandler();
                        m_reconnectHandler.BeginReconnect(m_session, m_reconnectPeriod * 1000, Server_ReconnectComplete);
                    }

                    return;
                }

                // update status.
                // 更新状态
                UpdateStatus(false, e.CurrentTime, "Connected [{0}]", session.Endpoint.EndpointUrl);

                // raise any additional notifications.
                // 触发保持成功状态的事件，相当于心跳机制确认
                m_KeepAliveComplete?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(application.ApplicationName, exception);
            }
        }

        /// <summary>
        /// Handles a reconnect event complete from the reconnect handler.
        /// 处理重连服务器之后的完成事件
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // ignore callbacks from discarded objects.
                // 如果事件已经被丢弃，就放弃处理
                if (!ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }

                m_session = m_reconnectHandler.Session;
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;

                // raise any additional notifications.
                // 触发链接完成的通知
                m_ReconnectComplete?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(application.ApplicationName, exception);//有待验证
            }
        }

        /// <summary>
        /// Raises the connect complete event on the main GUI thread.
        /// 引发一个连接完成事件
        /// </summary>
        private void DoConnectComplete(object state)
        {
            m_ConnectComplete?.Invoke(this, null);
        }

        /// <summary>
        /// 更新当前系统连接的状态
        /// </summary>
        /// <param name="error">指示该状态是否代表了一个错误</param>
        /// <param name="time">状态发生时的时间</param>
        /// <param name="status">状态消息</param>
        /// <param name="args">用于格式化状态消息的参数</param>
        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            OpcStausEventArgs e = new OpcStausEventArgs()
            {
                IsError = error,
                OccurTime = time.ToLocalTime(),
                Status = String.Format(status, args)
            };

            //触发事件
            m_OpcStatusChange?.Invoke(this, e);
        }

        private EndpointDescription SelectEndpoint(string discoveryUrl, bool useSecurity)
        {
            // needs to add the '/discovery' back onto non-UA TCP URLs.
            if (!discoveryUrl.StartsWith(Utils.UriSchemeOpcTcp))
            {
                if (!discoveryUrl.EndsWith("/discovery"))
                {
                    discoveryUrl += "/discovery";
                }
            }

            // parse the selected URL.
            Uri uri = new Uri(discoveryUrl);

            // set a short timeout because this is happening in the drop down event.
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;

            EndpointDescription selectedEndpoint = null;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (DiscoveryClient client = DiscoveryClient.Create(uri, configuration))
            {
                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);

                // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
                for (int ii = 0; ii < endpoints.Count; ii++)
                {
                    EndpointDescription endpoint = endpoints[ii];

                    // check for a match on the URL scheme.
                    if (endpoint.EndpointUrl.StartsWith(uri.Scheme))
                    {
                        // check if security was requested.
                        if (useSecurity)
                        {
                            if (endpoint.SecurityMode == MessageSecurityMode.None)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (endpoint.SecurityMode != MessageSecurityMode.None)
                            {
                                continue;
                            }
                        }

                        // pick the first available endpoint by default.
                        if (selectedEndpoint == null)
                        {
                            selectedEndpoint = endpoint;
                        }

                        // The security level is a relative measure assigned by the server to the 
                        // endpoints that it returns. Clients should always pick the highest level
                        // unless they have a reason not too.
                        if (endpoint.SecurityLevel > selectedEndpoint.SecurityLevel)
                        {
                            selectedEndpoint = endpoint;
                        }
                    }
                }

                // pick the first available endpoint by default.
                if (selectedEndpoint == null && endpoints.Count > 0)
                {
                    selectedEndpoint = endpoints[0];
                }
            }

            // if a server is behind a firewall it may return URLs that are not accessible to the client.
            // This problem can be avoided by assuming that the domain in the URL used to call 
            // GetEndpoints can be used to access any of the endpoints. This code makes that conversion.
            // Note that the conversion only makes sense if discovery uses the same protocol as the endpoint.

            Uri endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);

            if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
            {
                UriBuilder builder = new UriBuilder(endpointUrl);
                builder.Host = uri.DnsSafeHost;
                builder.Port = uri.Port;
                selectedEndpoint.EndpointUrl = builder.ToString();
            }

            // return the selected endpoint.
            return selectedEndpoint;
        }



        /// <summary>
        /// Handles a certificate validation error.
        /// 处理整数验证失败的情况
        /// </summary>
        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            try
            {
                e.Accept = m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates;

                if (!m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    e.Accept = true;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(application.ApplicationName, exception);
            }
        }

        #endregion








        #region 公开的读写操作

        /// <summary>
        /// ns=2;s=Devices/DynamicValue
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="url"></param>
        /// <returns></returns>
        public T ReadNode<T>(string url)
        {
            ReadValueId nodeToRead = new ReadValueId()
            {
                NodeId = new NodeId(url),
                AttributeId = Attributes.Value
            };
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection
            {
                nodeToRead
            };

            // read current value.
            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out DataValueCollection results,
                out DiagnosticInfoCollection diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            return  (T)results[0].Value;
        }


        #endregion



    }
}