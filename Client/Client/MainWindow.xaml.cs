﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace Client
{
    public class Applicazione
    {
        private String name;
        private uint process;
        private bool existIcon;
        private String bitmapBuf;
        private bool focus;

        public String Name
        {
            get { return name; }
            set { name = value;}
        }

        public uint Process
        {
            get { return process; }
            set { process = value; }
        }

        public bool Focus
        {
            get { return focus; }
            set { focus = value; }
        }
    }
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /* Variabili del client */
        private TcpClient client;                               //Fornisce connessioni client per i servizi di rete TCP.  
        private NetworkStream stream;                           //Stream per leggere dal server o scrivere
        private bool connesso = false;                          //Indica se la connessione è stata effettuata o meno
        private Thread listen;                                  //Thread in ascolto sul server
        private Thread sendKeys;                                //Thread che si occuperà di inviare la combinazione di tasti
        private Thread grafica;                                 //Thread che si occuperà di gestire il riassunto grafico
        private Dictionary<uint, Applicazione> applicazioni;    //Lista di applicazioni
        private string combinazione;

        public MainWindow()
        {
            InitializeComponent();
            client = null;
            stream = null;
            combinazione = null;
            applicazioni = new Dictionary<uint, Applicazione>();
        }

        ///<summary>
        ///Questo metodo gestisce la connessione al server all'indirizzo
        ///IP e alla porta forniti nelle textBox corrispondenti.
        ///Prima di iniziare controlla che questi campi contengano dati.
        ///In tal caso, inizia il tentativo di connessione. 
        ///Se il client è già connesso, provvede alla disconnessione.
        /// </summary>
        private void connetti_Click(object sender, RoutedEventArgs e)
        {
            if (!connesso)
            {
                testo.Text = "";
                if (string.IsNullOrWhiteSpace(indirizzo.Text))
                {
                    testo.AppendText("Il campo indirizzo non può essere vuoto.\n");
                    return;
                }
                if (string.IsNullOrWhiteSpace(porta.Text))
                {
                    testo.AppendText("Il campo porta non può essere vuoto.\n");
                    return;
                }
                    
                try
                {
                    IPAddress address = IPAddress.Parse(indirizzo.Text);
                    int port = int.Parse(porta.Text);
                    connetti.Content = "In corso...";
                    client = new TcpClient();
                    testo.AppendText("Connessione in corso...\n");
                    client.Connect(address, port);
                    connetti.Content = "Disconnetti";
                    testo.AppendText("Connesso!\n");
                    connesso = true;
                    stream = client.GetStream();
                    listen = new Thread(ListenServer);
                    listen.Start();
                }
                catch (Exception ex)
                {
                    testo.AppendText("Errore: " + ex.StackTrace + "\n");
                    if (client != null) {
                        client.Close();
                        client = null;
                    }
                    connesso = false;
                    connetti.Content = "Connetti";
                }
            }
            else
            {
                testo.AppendText("Disconnessione in corso...\n");
                listen.Abort();
                client.Close();
                client = null;
                connesso = false;
                connetti.Content = "Connetti";
                testo.Text = ""; 
            }
        }

        /// <summary>
        /// Il metodo ascolta il server fin tanto che il client vi è connesso
        /// All'apertura istanzia il buffer di lettura, poi se lo stream
        /// è disponibile, ne legge un byte che conterrà un codice identificativo
        /// dell'operazione:
        /// a -> nuova applicazione
        /// f -> focus cambiato
        /// r -> applicazione chiusa
        /// In tutti e tre i casi, legge la lunghezza del messaggio inviato
        /// e poi procede agli aggiornamenti individuati dalla notifica.
        /// </summary>
        private void ListenServer()
        {
            try
            {
                byte[] readBuffer = new byte[client.ReceiveBufferSize];
                int len;
                while (connesso)
                {
                    if (stream.CanRead)
                    {
                        stream.Read(readBuffer, 0, 4);                  //Lettura lunghezza messaggio
                        len = BitConverter.ToInt32(readBuffer, 0);      //Salvataggio lunghezza
                        stream.Read(readBuffer, 0, 2);                  //Lettura tipologia messaggio
                        char c = BitConverter.ToChar(readBuffer, 0);    //Salvataggio tipologia  
                        switch (c) {
                            case 'a':
                                stream.Read(readBuffer, 0, len);
                                string res = Encoding.Default.GetString(readBuffer);
                                Applicazione a = new Applicazione();
                                a = JsonConvert.DeserializeObject<Applicazione>(res);
                                applicazioni.Add(a.Process, a);
                                Action act = () => { testo.AppendText("Nome: " + a.Name + "\n"); };
                                testo.Dispatcher.Invoke(act);
                                break;
                            case 'f':
                                stream.Read(readBuffer, 0, len);
                                //Da implementare
                                break;
                            case 'r':
                                stream.Read(readBuffer, 0, len);    //len = DWORD
                                applicazioni.Remove(BitConverter.ToUInt32(readBuffer, 0));
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                testo.AppendText("Errore: " + ex.StackTrace);
            }
        }

        ///<summary>
        /// Questo metodo apre la finestra di scelta dei tasti da inviare
        /// all'applicazione in focus sul server. Attiva poi il thread che
        /// si occuperà dell'invio della combinazione di tasti.
        /// </summary>
        private void inviaApp_Click(object sender, RoutedEventArgs e)
        {
            if (!connesso)
            {
                testo.AppendText("Devi essere connesso per inviare combinazioni di tasti!\n");
                return;
            }
            Window1 w = new Window1();
            w.RaiseCustomEvent += new EventHandler<CustomEventArgs>(w_RaiseCustomEvent);
            w.Show();
            sendKeys = new Thread(inviaServer);
            sendKeys.Start();
        }

        void w_RaiseCustomEvent(object sender, CustomEventArgs e)
        {
            combinazione = e.Message;
            testo.AppendText("Combinazione: " + combinazione + "\n");
        }

        /// <summary>
        /// Il metodo riceve la stringa e la ripulisce modificando i tasti
        /// "particolari" nella combinazione corrispondente. Dopodiché,
        /// procede all'invio della combinazione corretta al server.
        /// </summary>
        private void inviaServer()
        {
            while (combinazione == null) ;

            string[] elem = combinazione.Split(' ');
            combinazione = "";
            foreach(var key in elem)
            {
                if (key.Equals("CTRL"))
                    combinazione += '^';
                else if (key.Equals("ALT"))
                    combinazione += '%';
                else if (key.Equals("SHIFT"))
                    combinazione += '+';
                else if (key.Equals("Backspace"))
                    combinazione += "{BS}";
                else if (key.Equals("Delete"))
                    combinazione += "{DEL}";
                else if (key.Equals("Esc"))
                    combinazione += "{ESC}";
                else if (key.Equals("Ins"))
                    combinazione += "{INS}";
                else if (key.Equals("Invio"))
                    combinazione += "~";
                else if (key.Equals("Fine"))
                    combinazione += "{END}";
                else if (key.Equals("Tab"))
                    combinazione += "{TAB}";
                else
                    combinazione += key;
            }
            byte[] sendBuffer = ASCIIEncoding.ASCII.GetBytes(combinazione);
            Action act = () => { testo.AppendText("Sto inviando la combinazione: " + combinazione + "\n"); };
            testo.Dispatcher.Invoke(act);
            stream.Write(sendBuffer, 0, sendBuffer.Length);
            return;
        }

        /// <summary>
        /// Questo metodo si occupa di aprire una finestra contenente
        /// il riassunto grafico dell'attività in corso sul server.
        /// </summary>
        private void open_Click(object sender, RoutedEventArgs e)
        {
            
        }
    }
}
