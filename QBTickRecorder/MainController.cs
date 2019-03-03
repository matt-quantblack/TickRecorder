using System;
using System.Net.Security;
using System.IO;
using System.Net.Sockets;
using Open_API_Library;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace QBTickRecorder
{
    public delegate void MessageHandler(string message);
    public delegate void StatusHandler(string message);
    public delegate void ErrorHandler(string message);
    public delegate void SymbolWriteMessageHandler(string symbolName, int symbolId, string dateString, bool isBid);
    public delegate void SymbolTickRequestHandler(Symbol symbol);
    public delegate void SymbolTickHandler(long symbolId, bool isBid, ulong value, DateTime tickTime);
    public delegate void ConnectionLostHandler();
    public delegate void HeartBeatHandler();

    public class MainController
    {
        
        //If access token expired then get a new one ehre
        //https://connect.spotware.com/apps/token?grant_type=refresh_token&refresh_token=9c48o7G6Xpaq1a1YvoizTki0hFmgGsIv7Ve9OWM6_Yk&client_id=485_wa8KPR3wR9Jf8ZgFEiRuBXi8wtCGLVCdNAQXoISPdOUCpZJSgg&%20client_secret=rdk7sIBd2w5sq8VUaNub9n2fb0zYoRpYaEkJfI2kt7hW2Ty2UD

        public Config Config { get; set; }
        public Dictionary<string, UserConfig> Users { get; set; }
        public MessageHandler MessageHandler { get; set; }
        public StatusHandler StatusHandler { get; set; }
        public ErrorHandler ErrorHandler { get; set; }
        public SymbolWriteMessageHandler SymbolWriteMessageHandler { get; set; }
        public SymbolTickRequestHandler SymbolTickRequestHandler { get; set; }
        public SymbolTickHandler SymbolTickHandler { get; set; }
        public ConnectionLostHandler ConnectionLostHandler {get; set;}
        public HeartBeatHandler HeartBeatHandler { get; set; }

        private TcpClient _tcpClient = new TcpClient();
        private SslStream _apiSocket;

        private Dictionary<string, Queue<TickData>> _ticksToWrite = new Dictionary<string, Queue<TickData>>();
        private Queue<ProtoMessage> _trasmitQueue = new Queue<ProtoMessage>();

        private System.Timers.Timer _heartbeatTimer;

        private volatile bool isShutdown = false;
        private int MaxMessageSize = 1000000;

        public MainController()
        {
            Config = null;

            _heartbeatTimer = new System.Timers.Timer();
            _heartbeatTimer.Interval = 10000;
            _heartbeatTimer.Enabled = false;
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Elapsed += new System.Timers.ElapsedEventHandler(HeartBeat);

            Users = new Dictionary<string, UserConfig>();

        }

        public void OpenConnection()
        {
            isShutdown = false;

            MessageHandler?.Invoke("Opening API connection.");

            //Start the listener thread to listen for Proto messages from the server
            _tcpClient = new TcpClient(Config.ApiHost, Config.ApiPort); ;
            _apiSocket = new SslStream(_tcpClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
            _apiSocket.AuthenticateAsClient(Config.ApiHost);

            StartListenThread();

            //use another thread to transmit a queue of Proto messages because there is a limit of 30 messages per second
            Thread transmitThread = new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                try
                {
                    //A continuos loop to transmit messages from a queue
                    Transmit();
                }
                catch (Exception e)
                {
                    ErrorHandler?.Invoke("Transmitter throws exception: " + e);
                    isShutdown = true;
                    //try to reconnect
                    ConnectionLostHandler?.Invoke();
                }
            });


            //Connection Flow:
            //Authorise the App
            //For each user account if accountID not loaded request from API
            //For each user account authorise the account
            //For each user account if symbols not loaded request from API
            //For each user account subscribe to all symbols
            //Start the heartbeat time

            //Authorise the application - when message recieved go to beginConnection
            var msgFactory = new OpenApiMessagesFactory();
            var msg = msgFactory.CreateAppAuthorizationRequest(Config.ClientId, Config.ClientSecret);
            _trasmitQueue.Enqueue(msg);

            transmitThread.Start();
        }

        private void StartListenThread()
        {
            Thread listenThread = new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                try
                {
                    //the listen loop, continuos loop to listen for Proto messages.
                    Listen(_apiSocket);
                }
                catch (System.IO.IOException ex)
                {
                    //connection lost so try and reconnect
                    ErrorHandler?.Invoke("IO Error: " + ex.Message);
                    StartListenThread();

                }
                catch (Exception e)
                {
                    ErrorHandler?.Invoke("Listener exception: " + e);
                    StartListenThread();
                }
            });

            listenThread.Start();
        }

        private void HeartBeat(object obj, System.Timers.ElapsedEventArgs e)
        {
            //keep connection alive with a heartbeat atleast every 10 seconds
            var msgFactory = new OpenApiMessagesFactory();
            var msg = msgFactory.CreateHeartbeatEvent();
            _trasmitQueue.Enqueue(msg);
            

            //stop the timer if the connection has died
            if (isShutdown)
                _heartbeatTimer.Stop();
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            ErrorHandler?.Invoke("Certificate error: " + sslPolicyErrors);
            return false;
        }

        private void Transmit()
        {
            while (!isShutdown)
            {
                if (_trasmitQueue.Count() > 0)
                {
                    //get the next message to submit
                    ProtoMessage msg = _trasmitQueue.Dequeue();

                    //Sends the Proto message
                    var msgByteArray = msg.ToByteArray();
                    byte[] length = BitConverter.GetBytes(msgByteArray.Length).Reverse().ToArray();
                    _apiSocket.Write(length);
                    _apiSocket.Write(msgByteArray);

                    
                    switch ((ProtoOAPayloadType)msg.PayloadType)
                    {
                        case ProtoOAPayloadType.PROTO_OA_APPLICATION_AUTH_REQ:
                            MessageHandler?.Invoke("Authorising App.");
                            break;
                        case ProtoOAPayloadType.PROTO_OA_ACCOUNT_AUTH_REQ:
                            var accAuth = ProtoOAAccountAuthReq.CreateBuilder().MergeFrom(msg.Payload).Build();
                            MessageHandler?.Invoke("Authorising account " + Users[accAuth.AccessToken].AccountId);
                            break;
                        case ProtoOAPayloadType.PROTO_OA_SUBSCRIBE_SPOTS_REQ:
                            var spotReq = ProtoOASubscribeSpotsReq.CreateBuilder().MergeFrom(msg.Payload).Build();
                            //get the associated user
                            UserConfig config = Users.Where(x => x.Value.AccountId == spotReq.CtidTraderAccountId).Select(x => x.Value).FirstOrDefault();
                            //get the associated symbol
                            Symbol symbol = config.Symbols.Where(x => x.Id == spotReq.GetSymbolId(0)).FirstOrDefault();
                            //Notify the spot request has been sent
                            SymbolTickRequestHandler?.Invoke(symbol);
                            break;

                
                    }
                    
                        
                }
                else if(!_heartbeatTimer.Enabled)
                {
                    //start the heartbeat timer
                    _heartbeatTimer.Enabled = true;
                    _heartbeatTimer.Start();
                }

                //Wait 2.01 seconds between each message as to not exceed the 30 messages per minute restriction
                Thread.Sleep(2010);
            }
        }

        private void BeginConnection()
        {
            //if account id is not loaded from saved then get account list - then go to load symbols
            foreach (UserConfig userConfig in Users.Values)
            {
                //create a queue of ticks to write as they are recieved
                _ticksToWrite.Add(userConfig.Token, new Queue<TickData>());

                if (userConfig.AccountId == 0)
                {
                    MessageHandler?.Invoke("Retrieving account id.");

                    //this will get all accountID's for the current Token (the token is related to a trading account that has authorised this app)
                    var msgFactory = new OpenApiMessagesFactory();
                    _trasmitQueue.Enqueue(msgFactory.CreateAccountListRequest(userConfig.Token));
                }
                else //otherwise go direct to GetSymbols
                    AuthAccount(userConfig.Token);
            }
                
        }

        private void AuthAccount(string userToken)
        {
            
            var msgFactory = new OpenApiMessagesFactory();
            _trasmitQueue.Enqueue(msgFactory.CreateAccAuthorizationRequest(userToken, Users[userToken].AccountId));
        }

        private void GetSymbols(long ctraderAccountId)
        {
            //get the associated user config
            UserConfig config = Users.Where(x => x.Value.AccountId == ctraderAccountId).Select(x=> x.Value).FirstOrDefault();

            if (config == null)
                ErrorHandler?.Invoke("Could not find account " + ctraderAccountId + ". Ticks will not be recorded for this account.");
            else
            {
                //if symbols aren't loaded then get symbols from api
                if (config.Symbols.Count == 0)
                {
                    MessageHandler?.Invoke("Retrieving symbol list for account " + ctraderAccountId + ".");

                    var msgFactory = new OpenApiMessagesFactory();
                    _trasmitQueue.Enqueue(msgFactory.CreateSymbolsListRequest(config.AccountId));
                }
                //otherwise go to StartSubscribes
                else
                    StartSubscribes(ctraderAccountId);
            }
        }

        private void StartSubscribes(long ctraderAccountId)
        {

            //get the associated user config
            UserConfig config = Users.Where(x => x.Value.AccountId == ctraderAccountId).Select(x => x.Value).FirstOrDefault();

            if (config == null)
            {
                ErrorHandler?.Invoke("Could not find account " + ctraderAccountId + ". Ticks will not be recorded for this account.");
                return;
            }

            //start subscribing to the tick data
            var msgFactory = new OpenApiMessagesFactory();
            foreach (Symbol s in config.Symbols)
            {
                _trasmitQueue.Enqueue(msgFactory.CreateSubscribeForSpotsRequest(ctraderAccountId, s.Id)); 
            }

            StartWrites(config);

        }

        private void StartWrites(UserConfig config)
        {
            //Start a tick writing thread to record all the recieved ticks that have been queued for this user
            Thread writeThread = new Thread(() =>
            {

                try
                {
                    WriteTicks(config);
                }
                catch (Exception e)
                {
                    //shutdown the background threads
                    ErrorHandler?.Invoke("Tick writer exception: " + e);

                    //wait one second and try to start the write thread again
                    Thread.Sleep(1000);
                    StartWrites(config);
                }

            });

            writeThread.Start();
        }

        private void WriteTicks(UserConfig config)
        {
            while (!isShutdown)
            {
                //batch tick writes to a maximum of 10000 to avoid a continuos loop from huge volumes of ticks in the queue
                List<TickData> collectedTicks = new List<TickData>();
                int tickCounter = 0;
                while (_ticksToWrite[config.Token].Count() > 0 && tickCounter < 10000)
                {
                    //Dequeue the tick and add it to the collected ticks list for writing
                    TickData data = _ticksToWrite[config.Token].Dequeue();
                    if (data != null)
                    {
                        collectedTicks.Add(data);
                    }
                    tickCounter++;
                }

                if (collectedTicks.Count > 0)
                {

                    //loop through each symbol as each symbols data is written in separate files
                    foreach (Symbol symbol in config.Symbols)
                    {
                        //group ticks by symbol, day and bid/ask and batch write these groups to file in one go.
                        var ticks = collectedTicks.Where(x => x.SymbolId == symbol.Id);

                        if (ticks.Count() == 0)
                            continue;

                        //create a dictionary where the key is the filename and the list is the list of ticks, on on each line, to write to the file.
                        Dictionary<string, string> fileWrites = new Dictionary<string, string>();

                        foreach (TickData tick in ticks)
                        {
                            //build the filename from the symbol name, day of tick and wither bid or ask
                            string key = tick.GetFilename(symbol.Name);

                            //if the key doesn't exist create a new key with an empty list to add the tick strings to
                            if (!fileWrites.ContainsKey(key))
                                fileWrites.Add(key, "");

                            fileWrites[key] += tick.ToString();
                        }

                        //There would have been a new key created for every day for both bid and ask ticks (most likely only 2 - 4 keys in total if ticks overalp 2 days)
                        //write all these to file
                        foreach (KeyValuePair<string, string> kvp in fileWrites)
                        {
                            string path = config.DataPath + kvp.Key;
                            if (!Directory.Exists(Path.GetDirectoryName(path)))
                                Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.AppendAllText(path, kvp.Value);

                            //extract the date out of the key to send to the SymbolWriteMessageHandler
                            string dateString = kvp.Key.Split(new char[] { '\\' })[1].Split(new char[] { '_' })[0];

                            SymbolWriteMessageHandler?.Invoke(symbol.Name, symbol.Id, dateString, kvp.Key.Contains("Bid"));
                        }
                    }
                }

                //Give some time to build up some ticks again
                Thread.Sleep(1000);
            }
        }

        private void Listen(SslStream sslStream)
        {
            
            while (!isShutdown)
            {
                //Read the message into a proto message
                Thread.Sleep(1);

                byte[] _length = new byte[sizeof(int)];
                int readBytes = 0;
                do
                {
                    Thread.Sleep(0);
                    readBytes += sslStream.Read(_length, readBytes, _length.Length - readBytes);
                } while (readBytes < _length.Length);

                int length = BitConverter.ToInt32(_length.Reverse().ToArray(), 0);
                if (length <= 0)
                    continue;

                if (length > MaxMessageSize)
                {
                    string exceptionMsg = "Message length " + length.ToString() + " is out of range (0 - " + MaxMessageSize.ToString() + ")";
                    throw new System.IndexOutOfRangeException();
                }

                byte[] _message = new byte[length];
                readBytes = 0;
                do
                {
                    Thread.Sleep(0);
                    readBytes += sslStream.Read(_message, readBytes, _message.Length - readBytes);
                } while (readBytes < length);
                var msgFactory = new OpenApiMessagesFactory();
                var protoMessage = msgFactory.GetMessage(_message);
                
                //recieved a msg so show View connection is still alive
                HeartBeatHandler?.Invoke();

                if (protoMessage.PayloadType > 49 && protoMessage.PayloadType < 54)
                {
                    switch((ProtoPayloadType)protoMessage.PayloadType)
                    {
                        case ProtoPayloadType.ERROR_RES:
                            ErrorHandler?.Invoke(protoMessage.ToString());
                            break;
                        case ProtoPayloadType.HEARTBEAT_EVENT:
                            //heartbeat Event
                            HeartBeatHandler?.Invoke();
                            break;
                        case ProtoPayloadType.PING_REQ:
                            MessageHandler?.Invoke("Ping req");
                            break;
                        case ProtoPayloadType.PING_RES:
                            MessageHandler?.Invoke("Ping res");
                            break;
                    }
                    
                }
                else
                {
                    //check what the message type is and perform the relevant operations
                    switch ((ProtoOAPayloadType)protoMessage.PayloadType)
                    {
                        case ProtoOAPayloadType.PROTO_OA_ERROR_RES:
                            //an error has been received
                            var error = ProtoOAErrorRes.CreateBuilder().MergeFrom(protoMessage.Payload).Build();
                            ErrorHandler?.Invoke("Proto message error " + error.ErrorCode + " " + error.Description);
                            break;
                        case ProtoOAPayloadType.PROTO_OA_ACCOUNT_AUTH_RES:
                            //auth has been recieved for the account
                            var auth = ProtoOAAccountAuthRes.CreateBuilder().MergeFrom(protoMessage.Payload).Build();

                            GetSymbols(auth.CtidTraderAccountId);
                            break;
                        case ProtoOAPayloadType.PROTO_OA_APPLICATION_AUTH_RES:
                            //Application has been authorised so continue the connection to get account and symbol data
                            MessageHandler?.Invoke("App authorised.");
                            BeginConnection();
                            break;
                        case ProtoOAPayloadType.PROTO_OA_SYMBOLS_LIST_RES:
                            //When requesting the list of all available assets
                            var symbols = ProtoOASymbolsListRes.CreateBuilder().MergeFrom(protoMessage.Payload).Build();

                            MessageHandler?.Invoke("Symbols downloaded for account " + symbols.CtidTraderAccountId);

                            //get the associated user
                            UserConfig config = Users.Where(x => x.Value.AccountId == symbols.CtidTraderAccountId).Select(x => x.Value).FirstOrDefault();

                            //store the symbols in a dictionary where the key is the id
                            foreach (ProtoOALightSymbol symbol in symbols.SymbolList)
                                config.Symbols.Add(new Symbol((int)symbol.SymbolId, symbol.SymbolName));

                            //Save to file so they can be easily reloaded on program restart
                            try
                            {
                                config.SaveToFile();
                            }
                            catch (IOException ex) //non critical so just flag an error
                            {
                                ErrorHandler?.Invoke("Could not save symbols list for account id " + symbols.CtidTraderAccountId + ": " + ex.Message);
                            }

                            //start subscribing to tick events
                            StartSubscribes(symbols.CtidTraderAccountId);

                            break;
                        case ProtoOAPayloadType.PROTO_OA_SPOT_EVENT:
                            //Tick has been recieved
                            var details = ProtoOASpotEvent.CreateBuilder().MergeFrom(protoMessage.Payload).Build();

                            //record the time of the tick in UTC time (the tick time doesn't actually come with the payload)
                            DateTime tickTime = DateTime.UtcNow;

                            //get the associated user
                            UserConfig config_spot = Users.Where(x => x.Value.AccountId == details.CtidTraderAccountId).Select(x => x.Value).FirstOrDefault();

                            //Queue this for writing to file - queue as TickData class which also has the time at which the tick was recieved
                            if (details.HasBid)
                            {
                                _ticksToWrite[config_spot.Token].Enqueue(new TickData((int)details.SymbolId, tickTime, true, details.Bid));

                                //Notify a tick has been recieved
                                SymbolTickHandler?.Invoke(details.SymbolId, true, details.Bid, tickTime);
                            }
                            if(details.HasAsk)
                            {
                                _ticksToWrite[config_spot.Token].Enqueue(new TickData((int)details.SymbolId, tickTime, false, details.Ask));

                                //Notify a tick has been recieved
                                SymbolTickHandler?.Invoke(details.SymbolId, false, details.Ask, tickTime);
                            }



                            break;
                        case ProtoOAPayloadType.PROTO_OA_GET_ACCOUNTS_BY_ACCESS_TOKEN_RES:

                            var accounts_list = ProtoOAGetAccountListByAccessTokenRes.CreateBuilder().MergeFrom(protoMessage.Payload).Build();

                            //get the first account - we only need 1 account to extract tick data - no trading will take place
                            ProtoOACtidTraderAccount account = accounts_list.CtidTraderAccountList.FirstOrDefault();

                            //assign the account id that will be used to extract ticks (Users are stored as a dictionary with token as the key)
                            if (account != null)
                                Users[accounts_list.AccessToken].AccountId = (long)account.CtidTraderAccountId;
                            else
                                throw new MissingFieldException("There are no trading accounts associated with this token.");

                            MessageHandler?.Invoke("Account selected: " + account.CtidTraderAccountId);

                            //Save to file so it can be easily reloaded on program restart
                            try
                            {
                                Config.SaveToFile();
                            }
                            catch (IOException ex) //non critical so just flag an error
                            {
                                ErrorHandler?.Invoke("Could not save config file with updated account id: " + ex.Message);
                            }

                            //get the symbols available to this account
                            AuthAccount(accounts_list.AccessToken);
                            break;
                        case ProtoOAPayloadType.PROTO_OA_SUBSCRIBE_SPOTS_RES:
                            var spotRes = ProtoOASubscribeSpotsRes.CreateBuilder().MergeFrom(protoMessage.Payload).Build();
                            break;
                        default:
                            ErrorHandler?.Invoke((ProtoOAPayloadType)protoMessage.PayloadType + " message not handled.");
                            break;

                    };
                }
            }
        }
    }
}
