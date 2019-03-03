using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using QBTickRecorder;

namespace QBTickRecorderForms
{
    public partial class MainForm : Form
    {
        MainController controller;

        Queue<string> _messages = new Queue<string>();
        Queue<string> _errors = new Queue<string>();

        private BindingList<SymbolDisplay> symbols;
        private BindingSource symbolsSource;

        DateTime lastMessage = new DateTime();

        private bool _shutdown = false;


        public MainForm()
        {
            InitializeComponent();

            symbols = new BindingList<SymbolDisplay>();
            symbolsSource = new BindingSource(symbols, null);
            

            dataGridViewSymbols.AutoGenerateColumns = false;

            DataGridViewColumn column1 = new DataGridViewTextBoxColumn();
            column1.DataPropertyName = "Symbol";
            column1.Name = "Symbol";
            dataGridViewSymbols.Columns.Add(column1);

            DataGridViewColumn column2 = new DataGridViewTextBoxColumn();
            column2.DataPropertyName = "Bid";
            column2.Name = "Bid";
            dataGridViewSymbols.Columns.Add(column2);

            DataGridViewColumn column3 = new DataGridViewTextBoxColumn();
            column3.DataPropertyName = "Ask";
            column3.Name = "Ask";
            dataGridViewSymbols.Columns.Add(column3);

            DataGridViewColumn column4 = new DataGridViewTextBoxColumn();
            column4.DataPropertyName = "LastTickString";
            column4.Name = "LastTick";
            column4.HeaderText = "Last Tick";
            column4.Width = 150;
            dataGridViewSymbols.Columns.Add(column4);

            DataGridViewColumn column5 = new DataGridViewTextBoxColumn();
            column5.DataPropertyName = "LastWriteString";
            column5.Name = "LastWrite";
            column5.HeaderText = "Last Write";
            column5.Width = 150;
            dataGridViewSymbols.Columns.Add(column5);

            dataGridViewSymbols.DataSource = symbolsSource;

            timerRefresh.Start();

            StartWrites();
            
        }

        void StartWrites()
        {
            System.Threading.Thread writeThread = new System.Threading.Thread(() =>
            {

                while (!_shutdown)
                {
                    try
                    {
                        string messages = "";
                        while (_messages.Count() > 0)
                        {
                            messages += _messages.Dequeue();
                        }

                        if (messages.Length > 0)
                        {
                            System.IO.File.AppendAllText("local/activity.log", messages);
                        }

                        string errors = "";
                        while (_errors.Count() > 0)
                        {
                            errors += _errors.Dequeue();
                        }

                        if (errors.Length > 0)
                        {
                            System.IO.File.AppendAllText("local/errors.log", messages);
                        }

                        System.Threading.Thread.Sleep(1000);
                    }
                    catch (Exception e)
                    {
                        //shutdown the background threads
                        DisplayError("Message writer exception: " + e);

                        //wait one second and try to start the write thread again
                        System.Threading.Thread.Sleep(1000);
                        StartWrites();
                    }
                }

            });

            writeThread.Start();

        }

        void InitController()
        {
            controller = new MainController();
            controller.MessageHandler = new MessageHandler(DisplayMessage);
            controller.ErrorHandler = new ErrorHandler(DisplayError);
            controller.ConnectionLostHandler = new ConnectionLostHandler(LostConnection);
            controller.SymbolWriteMessageHandler = new SymbolWriteMessageHandler(DisplaySymbolWrite);
            controller.SymbolTickRequestHandler = new SymbolTickRequestHandler(DisplayTickRequest);
            controller.SymbolTickHandler = new SymbolTickHandler(DisplayTick);
            controller.HeartBeatHandler = new HeartBeatHandler(HeartBeat);

            //local directory will store all user relevant data
            if (!System.IO.Directory.Exists("local"))
                System.IO.Directory.CreateDirectory("local");
            if (!System.IO.Directory.Exists("local//users"))
                System.IO.Directory.CreateDirectory("local//users");

            try
            {
                controller.Config = new Config(@"local/");
            }
            catch (Exception ex)
            {
                //if the config file can't be loaded terminate the program
                DisplayError("Unable to load required config file - " + ex.Message);
                return;
            }

            //Load all the user config files (need at least 1 user)
            string[] userFiles = System.IO.Directory.GetFiles(@"local/users/");
            foreach (string filename in userFiles)
            {
                UserConfig config = new UserConfig(filename);
                //store as a dictionary with access token as the key for easier referencing
                if (config != null)
                    controller.Users.Add(config.Token, config);
            }

            if (controller.Users.Count == 0)
            {
                DisplayError("Must have atleast 1 user config.txt file in the local/users/ directory");
            }
            else
            {
                //Start the Connection
                controller.OpenConnection();
                
            }
        }

        void HeartBeat()
        {
            toolStripStatusLabel.Text = "Quant Black Tick Recorder alive since: " + DateTime.Now.ToString();
            lastMessage = DateTime.UtcNow;
        }

        void LostConnection()
        {
            //recreate the controller and try and open the connection again
            InitController();
        }

        void DisplayTickRequest(Symbol symbol)
        {
            

            //run all of this in the GUI thread so the Listen thread doesn't not get bogged down  
            dataGridViewSymbols.Invoke((MethodInvoker)delegate
            {
                SymbolDisplay sd = symbols.Where(x => x.Symbol == symbol.Name).FirstOrDefault();
                if (sd == null || sd.Symbol == "")
                {
                    sd = new SymbolDisplay(symbol.Name, symbol.Id);
                }

                symbols.Add(sd);
            });

        }

        void DisplayTick(long symbolId, bool isBid, ulong value, DateTime tickTime)
        {

            lastMessage = DateTime.UtcNow;

            //run all of this in the GUI thread so the Listen thread doesn't not get bogged down   
            dataGridViewSymbols.Invoke((MethodInvoker)delegate
            {
                SymbolDisplay sd = symbols.Where(x => x.SymbolId == symbolId).FirstOrDefault();
                if (sd != null)
                {
                    double newVal = value / 100000.0;

                    if (isBid)
                    {
                        sd.Bid = newVal;
                    }
                    else
                    {
                        sd.Ask = newVal;
                    }

                    sd.LastTick = tickTime;
                    
                }

            });

        }

        void DisplayMessage(string message)
        {
            string timestampMessage = DateTime.Now.ToString() + ": " + message;

            _messages.Enqueue(timestampMessage);
            listBoxMessages.Invoke((MethodInvoker)delegate {
                // Running on the UI thread
                listBoxMessages.Items.Add(timestampMessage);
             });

        }

        void DisplayError(string message)
        {
            string timestampMessage = DateTime.Now.ToString() + ": " + message;

            _errors.Enqueue(timestampMessage);
            listBoxErrors.Invoke((MethodInvoker)delegate {
                // Running on the UI thread
                listBoxErrors.Items.Add(timestampMessage);
            });
        }

        void DisplaySymbolWrite(string symbolName, int symbolId, string dateString, bool isBid)
        {
            //run all of this in the GUI thread so the Listen thread doesn't not get bogged down   
            dataGridViewSymbols.Invoke((MethodInvoker)delegate
            {
                SymbolDisplay sd = symbols.Where(x => x.SymbolId == symbolId).FirstOrDefault();
                if (sd != null)
                {
                    sd.LastWrite = DateTime.Now;
                }

            });
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            InitController();
        }

        private void timerRefresh_Tick(object sender, EventArgs e)
        {
            //refresh the symbols list every 500 milliseconds
            dataGridViewSymbols.Refresh();

            if ((DateTime.UtcNow - lastMessage).TotalSeconds > 40)
            {
                toolStripStatusLabelConnected.Text = "Not Connected";
                toolStripStatusLabelConnected.ForeColor = System.Drawing.Color.Red;
            }
            else
            {
                toolStripStatusLabelConnected.Text = "CONNECTED";
                toolStripStatusLabelConnected.ForeColor = System.Drawing.Color.Green;
            }
        }

   
    }
}
