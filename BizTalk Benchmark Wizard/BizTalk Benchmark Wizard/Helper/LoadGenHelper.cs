﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using LoadGen;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Configuration;
using System.ServiceModel.Configuration;


namespace BizTalk_Benchmark_Wizard.Helper
{
    internal class LoadGenHelper
    {
        public delegate void CompleteHandler(object sender, LoadGenStopEventArgs e);
        public event CompleteHandler OnComplete;
        public double TestDuration = 120;
        public List<PerfCounter> PerfCounters = new List<PerfCounter>();
        List<LoadGen.LoadGen> _loadGenClients = new List<LoadGen.LoadGen>();
        int _numberOfLoadGenStopped = 0;
        int _numberOfLoadGenClients = 0;
        List<LoadGenStopEventArgs> _allLoadGenStopEventArgs = new List<LoadGenStopEventArgs>();
        
        public LoadGenHelper()
        {
            
        }
        
        public void RunTests(Environment environment, List<HostMaping> hostmappings, List<string> servers)
        {
            foreach (string server in servers)
            {
                PerfCounter perfCounter = new PerfCounter();
                perfCounter.Server = server;
                _numberOfLoadGenClients++;
                foreach (HostMaping hostMapping in hostmappings.Where(h=>h.SelectedHost==server))
                {
                    switch (hostMapping.HostName)
                    {
                        case "BBW_RxHost":
                            perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_RxHost", server));
                            perfCounter.HasReceiveCounter = true;
                            break;
                        case "BBW_PxHost":
                            break;
                        case "BBW_TxHost":
                            perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_TxHost", server));
                            perfCounter.HasProcessingCounter = true;
                            break;
                    }
                }
                perfCounter.CPUCounters.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", server));
                PerfCounters.Add(perfCounter);
            }

            string rcvHost = hostmappings.First(h => h.HostName == "BBW_RxHost").SelectedHost;

            _loadGenClients.Add(CreateAndStartLoadGenClient(CreateLoadGenScript(environment.LoadGenScripfile, rcvHost), rcvHost));
        }
        public void StopAllTests()
        {
            foreach (LoadGen.LoadGen loadGen in _loadGenClients)
                loadGen.Stop();
        }
        public bool TestIndigoService(string server)
        {
            try
            {
                IndigoService.ServiceTwoWaysVoidNonTransactionalClient proxy = new BizTalk_Benchmark_Wizard.IndigoService.ServiceTwoWaysVoidNonTransactionalClient("netTcpBinding_IServiceTwoWaysVoidNonTransactional");

                System.ServiceModel.EndpointAddress newAddress =
                    new System.ServiceModel.EndpointAddress(string.Format("{0}://{1}:{2}{3}",
                        proxy.Endpoint.Address.Uri.Scheme,
                        server,
                        proxy.Endpoint.Address.Uri.Port.ToString(),
                        proxy.Endpoint.Address.Uri.AbsolutePath));

                proxy.Endpoint.Address = newAddress;
                
                string xml = "<Response><resp>This is a response</resp></Response>";

                using (proxy as IDisposable)
                {
                    System.ServiceModel.Channels.MessageVersion version = System.ServiceModel.Channels.MessageVersion.Soap12WSAddressing10;
                    MemoryStream stream = new MemoryStream(Encoding.Default.GetBytes(xml), 0, Encoding.Default.GetBytes(xml).Length);
                    stream.Seek(0L, SeekOrigin.Begin);
                    XmlTextReader reader = new XmlTextReader(stream);
                    System.ServiceModel.Channels.Message request = System.ServiceModel.Channels.Message.CreateMessage(version, "http://tempuri.org/IServiceTwoWaysVoidNonTransactional/ConsumeMessage", (XmlReader)reader);
                    

                    proxy.ConsumeMessage(request);
                }
            }
            catch(Exception ex)
            {
                return false;
            }
            return true;
        }
        
        private string CreateLoadGenScript(string template, string server)
        {
            string rootPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Resources\\LoadGenScripts");
            
            string newScriptFile=Path.Combine(rootPath, server+"_LoadGenScript.xml");
            if(File.Exists(newScriptFile))
                File.Delete(newScriptFile);
            
            StreamWriter writer =new StreamWriter(newScriptFile);

            using (StreamReader reader = new StreamReader(Path.Combine(rootPath, template))) 
            {
                while (reader.Peek() >= 0) 
                {
                    string newLine = reader.ReadLine();
                    newLine=newLine.Replace("@ServerName", server);
                    newLine=newLine.Replace("@FilePath", rootPath);
                    writer.WriteLine(newLine);
                }
            }
            writer.Close();
            return newScriptFile;
        }
        private LoadGen.LoadGen CreateAndStartLoadGenClient(string scriptFile, string server)
        {
            LoadGen.LoadGen loadGen = null;
            try
            {
                UpdateServiceAddress(server, "ClientEndPoint1");

                XmlDocument doc = new XmlDocument();
                doc.Load(scriptFile);
                TestDuration = long.Parse( doc.SelectSingleNode("LoadGenFramework/CommonSection/StopMode/TotalTime").InnerText);

                if (string.Compare(doc.FirstChild.Name, "LoadGenFramework", true, new CultureInfo("en-US")) != 0)
                {
                    throw new ConfigException("LoadGen Configuration File Schema Invalid!");
                }

                _numberOfLoadGenClients++;
                loadGen = new LoadGen.LoadGen(doc.FirstChild);
                loadGen.LoadGenStopped += new LoadGenEventHandler(LoadGen_Stopped);
                loadGen.Start();
            }
            catch (ConfigException cex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw;
            }

            return loadGen;
        }
        private void CreateCounterCollectors(string server)
        {
            PerfCounter perfCounter = new PerfCounter();
            perfCounter.Server = server;

            //perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_PxHost", server));
            //perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_RxHost", server));
            perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_TxHost", server));

            //perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_PxHost", server));
            perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_RxHost", server));
            //perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_TxHost", server));

            perfCounter.CPUCounters.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", server));
            
            PerfCounters.Add(perfCounter);
        }
        protected void RaiseCompleteEvent(object sender, LoadGenStopEventArgs e)
        {
            if (OnComplete != null)
            {
                OnComplete(sender, e);
            }
        }
        private void LoadGen_Stopped(object sender, LoadGenStopEventArgs e)
        {
            //TimeSpan span1 = e.LoadGenStopTime.Subtract(e.LoadGenStartTime);
            //this._ctx.LogInfo("FilesSent: " + e.NumFilesSent);
            //this._ctx.LogInfo("StartTime: " + e.LoadGenStartTime);
            //this._ctx.LogInfo("StopTime:  " + e.LoadGenStopTime);
            //this._ctx.LogInfo("DeltaTime: " + span1.TotalSeconds + "Secs.");
            //this._ctx.LogInfo("Rate:      " + ((e.NumFilesSent) / span1.TotalSeconds));

            //bExitApp = true;
            _allLoadGenStopEventArgs.Add(e);
            _numberOfLoadGenStopped++;
        
            if (_numberOfLoadGenClients == _numberOfLoadGenStopped)
            {
                try
                {
                    long numberOfMsgsSent = _allLoadGenStopEventArgs.Sum(l => l.NumFilesSent);
                    DateTime startTime = _allLoadGenStopEventArgs.Min(l => l.LoadGenStartTime);
                    DateTime stopTime = e.LoadGenStopTime;

                    LoadGenStopEventArgs ea = new LoadGenStopEventArgs(numberOfMsgsSent, startTime, stopTime);
                    RaiseCompleteEvent(sender, ea);
                }
                catch (Exception ex)
                {
                    RaiseCompleteEvent(this, new LoadGenStopEventArgs(1, DateTime.Now, DateTime.Now));
                }
            }
        }
        private void UpdateServiceAddress(string address, string endpointName)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(System.Reflection.Assembly.GetExecutingAssembly().Location);

            ClientSection clientSection = (ClientSection)config.GetSection("system.serviceModel/client");
            ChannelEndpointElement endpointElement = null;
            foreach (ChannelEndpointElement element in clientSection.Endpoints)
            {
                if (element.Name == endpointName)
                {
                    endpointElement = element;
                    break;
                }
            }
            if (endpointElement != null)
            {
                endpointElement.Address = new Uri(string.Format("{0}://{1}:{2}{3}",
                            endpointElement.Address.Scheme,
                            address,
                            endpointElement.Address.Port.ToString(),
                            endpointElement.Address.AbsolutePath));


                config.Save();
                ConfigurationManager.RefreshSection("system.serviceModel/client");


            }
            else
            {
                throw new ApplicationException(string.Format("Could not find {0} endpoint configuration section", endpointName));
            }
        }
        


    }
    public class PerfCounter
    {
        public bool HasProcessingCounter = false;
        public bool HasReceiveCounter = false;
        
        public List<PerformanceCounter> ProcessedCounters = new List<PerformanceCounter>();
        public List<PerformanceCounter> ReceivedCounters = new List<PerformanceCounter>();
        public List<PerformanceCounter> CPUCounters = new List<PerformanceCounter>();

        public float ProcessedCounterValue
        {
            get 
            {
                float ret = 1;
                foreach (PerformanceCounter c in this.ProcessedCounters)
                    ret += c.NextValue();
                return ret;
            }
        }
        public float ReceivedCounterValue
        {
            get
            {
                float ret = 1;
                foreach (PerformanceCounter c in this.ReceivedCounters)
                    ret += c.NextValue();
                return ret;
            }
        }
        public float CPUCounterValue
        {
            get
            {
                float ret = 1;
                foreach (PerformanceCounter c in this.CPUCounters)
                    ret += c.NextValue();
                return ret;
            }
        }
        public string Server = string.Empty;
    }
    [System.ServiceModel.ServiceContract]
    public interface IServiceTwoWaysVoidNonTransactional
    {
        // Methods
        [System.ServiceModel.OperationContract(Action = "*")]
        void ConsumeMessage(System.ServiceModel.Channels.Message msg);
    }


}