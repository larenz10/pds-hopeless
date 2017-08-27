﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace Client
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyCollectionChanged
    {
        private TcpClient Client { get; set; }                          //Fornisce connessioni client per i servizi di rete TCP.
        private NetworkStream Stream { get; set; }                      //Stream per leggere e scrivere dal server
        private bool Connesso { get; set; }                             //Variabile booleana che identifica la connessione attiva
        private Thread Ascolta { get; set; }                            //Thread in ascolto sul server
        private Thread Riassunto { get; set; }                          //Thread che gestisce il riassunto
        private ObservableCollection<Applicazione> Apps { get; set; }   //Collezione che contiene le applicazioni del server
        private string Combinazione { get; set; }                       //Combinazione da inviare
        private uint ProcessoFocus { get; set; }                        //Codice del processo in focus
        private Stopwatch TempoC { get; set; }                          //Cronometro di connessione al server
        readonly object key = new object();
        private object _lock = new object();

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public MainWindow()
        {
            InitializeComponent();
            this.Show();
            Thread.CurrentThread.Name = "Main";
            SetUp();
        }

        private void SetUp()
        {
            Client = null;
            Stream = null;
            Connesso = false;
            Combinazione = null;
            ProcessoFocus = 0;
            Apps = new ObservableCollection<Applicazione>();
            BindingOperations.EnableCollectionSynchronization(Apps, _lock);
            listView.ItemsSource = Apps;
            TempoC = new Stopwatch();
        }

        /// <summary>
        /// Questo metodo gestisce la connessione al server all'indirizzo
        /// IP e alla porta forniti nelle textBox corrispondenti.
        /// Prima di iniziare controlla che questi campi contengano dati.
        /// In tal caso, inizia il tentativo di connessione. 
        /// Alla fine della connessione attiva i thread paralleli che si
        /// occupano delle altre funzioni richieste.
        /// Se il client è già connesso, provvede alla disconnessione.
        /// </summary>
        private void connetti_Click(object sender, RoutedEventArgs e)
        {
            if (!Connesso)
            {
                testo.Text = "";
                if (string.IsNullOrWhiteSpace(indirizzo.Text))
                {
                    testo.AppendText("Il campo Indirizzo non può essere vuoto!\n");
                    return;
                }
                if (string.IsNullOrWhiteSpace(porta.Text))
                {
                    testo.AppendText("Il campo Porta non può essere vuoto.\n");
                    return;
                }
                try
                {
                    IPAddress ind = IPAddress.Parse(indirizzo.Text);
                    int port = int.Parse(porta.Text);
                    Client = new TcpClient();
                    var result = Client.BeginConnect(ind, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    if (success)
                    {
                        TempoC.Restart();
                        connetti.Content = "Disconnetti";
                        testo.AppendText("Connesso a " + indirizzo.Text + ":" + porta.Text + ".\n");
                        Connesso = true;
                        Stream = Client.GetStream();
                        Ascolta = new Thread(ascoltaServer);
                        Ascolta.IsBackground = true;
                        Ascolta.Start();
                        Riassunto = new Thread(monitora);
                        Riassunto.IsBackground = true;
                        Riassunto.Start();
                        indirizzo.IsReadOnly = true;
                        porta.IsReadOnly = true;
                        //controllaConnessione();
                    }
                    else
                    {
                        testo.AppendText("Connessione fallita.\n");
                    }
                }
                catch (FormatException ex)
                {
                    testo.AppendText("Errore nella specifica di indirizzo IP o porta.\n");
                    testo.AppendText(ex.StackTrace);
                }
                catch (Exception ex)
                {
                    testo.AppendText("Errore: " + ex.StackTrace);
                    if (Client != null)
                    {
                        Client.Close();
                        Client = null;
                    }
                    if (Ascolta != null)
                        Ascolta.Abort();
                    if (Riassunto != null)
                        Riassunto.Abort();
                    Connesso = false;
                    connetti.Content = "Connetti";
                }
            }
            else
            {
                TempoC.Stop();
                Connesso = false;
                Ascolta.Abort();
                Riassunto.Join();
                Client.Close();
                ProcessoFocus = 0;
                Client = null;
                connetti.Content = "Connetti";
                indirizzo.IsReadOnly = false;
                porta.IsReadOnly = false;
            }
        }

        /// <summary>
        /// Il metodo permette al thread principale di controllare
        /// se il socket ha ancora una connessione attiva al server.
        /// In caso di disconnessione brutale da parte del server,
        /// avvia il processo di disconnessione.
        /// </summary>
        private void controllaConnessione()
        {
            while (Client.Connected) ;
            testo.AppendText("Il server si è disconnesso.\n");
            TempoC.Stop();
            Connesso = false;
            Ascolta.Abort();
            Ascolta.Join();
            Riassunto.Join();
            Client.Close();
            Client = null;
            connetti.Content = "Connetti";
            indirizzo.IsReadOnly = false;
            porta.IsReadOnly = false;
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
        private void ascoltaServer()
        {
            Thread.CurrentThread.Name = "Ascolto";
            bool pronto = false;
            try
            {
                byte[] readBuffer = new byte[Client.ReceiveBufferSize];
                int len;
                while (Connesso)
                {
                    if (Stream.CanRead)
                    {
                        Stream.Read(readBuffer, 0, 4);
                        len = BitConverter.ToInt32(readBuffer, 0);
                        Array.Clear(readBuffer, 0, 4);
                        Stream.Read(readBuffer, 0, 1);
                        char c = BitConverter.ToChar(readBuffer, 0);
                        Array.Clear(readBuffer, 0, 1);
                        switch (c)
                        {
                            //Inserimento nuova applicazione
                            //Messaggio: len-'a'-JSON(App)
                            case 'a':
                                Stream.Read(readBuffer, 0, len);
                                string res = Encoding.Default.GetString(readBuffer);
                                Array.Clear(readBuffer, 0, len);
                                Applicazione a = new Applicazione();
                                a = JsonConvert.DeserializeObject<Applicazione>(res);
                                a.TempoF = new Stopwatch();
                                /*if (a.BitmaskBuffer != null)
                                {
                                    IntPtr bitmask = new IntPtr(Convert.ToInt32(a.BitmaskBuffer));
                                    if (a.ColorBuffer != null)
                                    {
                                        IntPtr color = new IntPtr(Convert.ToInt32(a.ColorBuffer));
                                        a.Icona = System.Drawing.Image.FromHbitmap(bitmask, color);
                                    }
                                    else
                                        a.Icona = System.Drawing.Image.FromHbitmap(bitmask);
                                    
                                }*/
                                if (a.Focus)
                                {
                                    if (ProcessoFocus != 0)
                                    {
                                        Apps.Where(i => i.Process == ProcessoFocus).Single().Focus = false;
                                        Apps.Where(i => i.Process == ProcessoFocus).Single().TempoF.Stop();
                                    }
                                    a.TempoF.Start();
                                    ProcessoFocus = a.Process;
                                }
                                Apps.Add(a);
                                if (!pronto)
                                {
                                    pronto = true;
                                    lock (key)
                                        Monitor.Pulse(key);
                                }
                                break;
                            //Rimozione di un'applicazione
                            //Messaggio: len-'r'-Codice processo App
                            case 'r':
                                Stream.Read(readBuffer, 0, len);
                                uint prox = BitConverter.ToUInt32(readBuffer, 0);
                                if (Apps.Where(i => i.Process == prox).Single().Focus == true)
                                    ProcessoFocus = 0;
                                Apps.Remove(Apps.Where(i => i.Process == prox).Single());
                                break;
                            //Focus cambiato
                            //Messaggio: len-'f'-Codice processo App
                            case 'f':
                                Stream.Read(readBuffer, 0, len);
                                uint proc = BitConverter.ToUInt32(readBuffer, 0);
                                Apps.Where(i => i.Process == ProcessoFocus).Single().Focus = false;
                                Apps.Where(i => i.Process == ProcessoFocus).Single().TempoF.Stop();
                                Apps.Where(i => i.Process == proc).Single().Focus = true;
                                Apps.Where(i => i.Process == proc).Single().TempoF.Start();
                                ProcessoFocus = proc;
                                break;
                            default:
                                break;
                        }
                    }
                }
                return;
            }
            catch (IOException ex)
            {
                Action action = () => testo.AppendText(ex.StackTrace);
                Dispatcher.Invoke(action);
            }
            catch(Exception ex)
            {
                Action action = () => testo.AppendText(ex.StackTrace);
                Dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Il metodo gestisce il riassunto grafico, aggiornando con
        /// un thread dedicato le informazioni nel datagrid in base
        /// alle informazioni ricevute dal server, che sono trasmesse
        /// alla struttura applicazioni. Ogni volta che in quella struttura
        /// si verificherà una modifica, questa sarà riportata sul riassunto.
        /// </summary>
        private void monitora()
        {
            Thread.CurrentThread.Name = "Riassunto";
            //Finché non ci sono applicazioni nel dizionario è inutile monitorare.
            lock (key)
                Monitor.Wait(key);
            //Il monitoraggio dura fino alla fine della connessione
            while (Connesso)
            {
                if (ProcessoFocus != 0)
                {
                    Applicazione app = Apps.Where(i => i.Process == ProcessoFocus).SingleOrDefault();
                    if (app != null)
                    {
                        app.Percentuale = (app.TempoF.Elapsed.TotalMilliseconds / TempoC.Elapsed.TotalMilliseconds) * 100;
                    }
                }
            }
            //Alla fine svuota la tabella
            Apps.Clear();
            return;
        }

        /// <summary>
        /// Questo metodo fa partire il thread che si occupa
        /// di inviare i tasti all'applicazione in focus 
        /// e aspetta la sua terminazione. Dopodiché chiama
        /// la funzione invioFocus, che gestisce l'invio al 
        /// server della combinazione.
        /// </summary>
        private void invia_Click(object sender, RoutedEventArgs e)
        {
            if (!Connesso)
            {
                testo.AppendText("Devi essere connesso per poter inviare tasti all'applicazione in focus!\n");
                return;
            }
            Window1 w = new Window1();
            w.RaiseCustomEvent += new EventHandler<CustomEventArgs>(w_RaiseCustomEvent);
            w.ShowDialog();
            Thread inviaTasti = new Thread(invioFocus);
            inviaTasti.IsBackground = true;
            inviaTasti.Start();
        }

        /// <summary>
        /// Il metodo si occupa di inviare la combinazione
        /// all'applicazione in focus.
        /// </summary>
        private void invioFocus()
        {
            Thread.CurrentThread.Name = "Invio";
            if (Combinazione != null)
            {
                byte[] cod_proc = BitConverter.GetBytes(ProcessoFocus);
                byte[] codifica = Encoding.ASCII.GetBytes(Combinazione);
                int lung = codifica.Length;
                byte[] cod_lung = BitConverter.GetBytes(lung);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(cod_lung);
                Action act = () => { testo.AppendText("Sto inviando la combinazione all'applicazione in focus.\n"); };
                Dispatcher.Invoke(act);
                Stream.Write(cod_lung, 0, 4);
                Stream.Write(cod_proc, 0, 4);
                Stream.Write(codifica, 0, lung);
            }
            return;
        }


        /// <summary>
        /// Evento che si occupa di prendere la combinazione codificata da Window1.
        /// </summary>
        void w_RaiseCustomEvent(object sender, CustomEventArgs e)
        {
            Combinazione = e.Message;
            testo.AppendText("Combinazione: " + Combinazione + ".\n");
        }

    }
}
