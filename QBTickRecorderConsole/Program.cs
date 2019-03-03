using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using QBTickRecorder;

namespace QBTickRecorderConsole
{
    class Program
    {
        static MainController controller;

        static bool ProgramRunning;

        static Queue<string> _messageQueue = new Queue<string>();
        static Queue<string> _errorQueue = new Queue<string>();


        static void Main(string[] args)
        {

            ProgramRunning = true;
            
            controller = new MainController();
            controller.MessageHandler = new MessageHandler(DisplayMessage);
            controller.ErrorHandler = new ErrorHandler(DisplayError);
            controller.ConnectionLostHandler = new ConnectionLostHandler(LostConnection);
            controller.SymbolWriteMessageHandler = new SymbolWriteMessageHandler(DisplaySymbolWrite);
            controller.SymbolTickRequestHandler = new SymbolTickRequestHandler(DisplayTickRequest);
            controller.HeartBeatHandler = new HeartBeatHandler(HeartBeat);

            Console.WriteLine("Starting CTrader API Tick Recorder");

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
                Console.ReadLine();
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
                Console.WriteLine(controller.Config.ToString());
                controller.OpenConnection();

                //Use a seperate thread to write from a message queue
                //This should be the only thread to Dequeue from the message queus
                Thread t = new Thread(() => {
                    //make sure this thread exits when the program exits
                    Thread.CurrentThread.IsBackground = true;
                    try
                    {
                        WriteMessages();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("No more messages or errors will be recorded. Message thread has failed: " + ex.Message);
                    }
                });
                t.Start();
            }

            Console.ReadLine();
        }

        static void HeartBeat()
        {
            Console.Write(".");
        }

        static void LostConnection()
        {
            //try and reopen the connection
            Main(null);
        }

        static void DisplayTickRequest(Symbol symbol)
        {
            string msg = DateTime.Now.ToString() + ": " + symbol.Name + " spots requested.";
            Console.WriteLine("\n" + msg);
            _messageQueue.Enqueue(msg);
        }

        static void DisplayMessage(string message)
        {
            string msg = DateTime.Now.ToString() + ": " + message;
            Console.WriteLine("\n" + msg);
            _messageQueue.Enqueue(msg);


        }

        static void DisplayError(string message)
        {
            string error = DateTime.Now.ToString() + ": " + message;
            Console.WriteLine("\n" + error);
            _errorQueue.Enqueue(error);

        }

        static void DisplaySymbolWrite(string symbolName, int symbolId, string dateString, bool isBid)
        {
            string feedType = "Ask";
            if (isBid)
                feedType = "Bid";

            string message = symbolName + " " + dateString + " " + feedType;
            Console.Write("\rWriting ticks for " + message + "\t");

        }

        static void WriteMessages()
        {
            while (ProgramRunning)
            {
                //batch write data from the messages queue
                int count = 0;
                string content = "";
                //limit this loop to 10000 messages at a time
                while (_messageQueue.Count > 0 && count < 10000)
                {
                    string message = _messageQueue.Dequeue();
                    content += message;
                    count++;
                }
                //write a batch of messages
                try
                {
                    System.IO.File.AppendAllText(@"local\activity.log", content);
                }
                catch (Exception ex)
                {
                    DisplayError("Could not record messages. " + ex.Message);
                }

                //batch write data from the errors queue
                count = 0;
                content = "";
                //limit this loop to 10000 messages at a time
                while (_errorQueue.Count > 0 && count < 10000)
                {
                    string error = _errorQueue.Dequeue();
                    content += error;
                    count++;
                }
                //write a batch of messages
                try
                {
                    System.IO.File.AppendAllText(@"local\errors.log", content + "\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not record errors. " + ex.Message);
                }

                //Wait 1 second before trying to write again
                Thread.Sleep(1000);
            }

        }
    }
}
