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
        public String name;
        public uint process;
        public bool existIcon;
        public String bitmapBuf;
        public String focus;
    }
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /* Variabili del client */
        private TcpClient client;           //Fornisce connessioni client per i servizi di rete TCP.  
        private NetworkStream stream;       //Stream per leggere dal server o scrivere
        private bool connesso = false;      //Indica se la connessione è stata effettuata o meno
        private Thread listen;              //Thread in ascolto sul server
        private List<Applicazione> applicazioni;    //Lista di applicazioni

        public MainWindow()
        {
            InitializeComponent();
            client = null;
            stream = null;
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

        private void ListenServer()
        {
            try
            {
                byte[] readBuffer = new byte[client.ReceiveBufferSize];
                while (connesso)
                {
                    if (stream.CanRead)
                    {
                        stream.Read(readBuffer, 0, sizeof(uint));
                        int len = readBuffer[0];
                        stream.Read(readBuffer, 0, len);
                        string res = Encoding.Default.GetString(readBuffer);
                        Applicazione a = JsonConvert.DeserializeObject<Applicazione>(res);
                        applicazioni.Add(a);
                        testo.AppendText("Nome: " + a.name + "\n");
                    }
                }
            }
            catch (IOException ex)
            {
                testo.AppendText("Errore: " + ex.StackTrace);
            }
        }

        ///<summary>
        /// Questo metodo si occupa di inviare all'applicazione attualmente
        /// in focus una sequenza di tasti composta da zero o più modificatori
        /// (CTRL/ALT/CANC) seguiti dal keycode corrispondente.
        /// </summary>
        private void inviaApp_Click(object sender, RoutedEventArgs e)
        {
            if (!connesso)
            {
                testo.AppendText("Devi essere connesso per inviare combinazioni di tasti!\n");
                return;
            }
            testo.AppendText("Metodo non implementato.\n");
        }



        /* Questo metodo si occupa di inviare al server il messaggio contenuto
         * nel campo corrispondente. Se non è connesso, ritorna immediatamente.
         * Aspetta la risposta dal server. 
         * Non implementato perché inutile nell'ambito del progetto.
         * Utilizzato solo per test preliminari.
                  
        private void invia_Click(object sender, RoutedEventArgs e)
        {
            if (!connected) {
                testo.AppendText("Devi essere connesso per scambiare messaggi!\n");
                return;
            }
            try
            {
                byte[] buffer = Encoding.ASCII.GetBytes(messaggio.Text);
                stream = client.GetStream();
                stream.Write(buffer, 0, buffer.Length);
                testo.AppendText("Messaggio inviato al server.\n");
                int totali = 0;         //byte ricevuti finora
                int ricevuti = 0;       //byte ricevuti all'ultima lettura

                while (totali < buffer.Length)
                {
                    // Gestione chiusura preventiva
                    if ((ricevuti = stream.Read(buffer, totali,
                        buffer.Length - totali)) == 0)
                    {
                        testo.AppendText("La connessione si è chiusa prematuramente.\n");
                        connetti_Click(sender, e);
                        break;
                    }
                    totali += ricevuti;
                }
                testo.AppendText("Ricevuti " + totali + " bytes dal server.\n");
                testo.AppendText(Encoding.ASCII.GetString(buffer, 0, totali));
            } catch (Exception ex)
            {
                testo.AppendText("Errore: " + ex.StackTrace);
                client.Close();
                client = null;
                stream.Close();
                stream = null;
                connected = false;
                connetti.Content = "Connetti";
            }
        } 
        */
    }
}
