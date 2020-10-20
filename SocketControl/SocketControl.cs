using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace SocketControl
{
    public delegate void RecieveMsg(object sender, byte[] rcv, int rcvlen);
    public delegate void SocketDisconnect(object sender, System.EventArgs e);
    public delegate void ShowExceptionMsg(object sender, string msg);

    public class SocketControls
    {
        bool bstop = false;

        public Socket clientSocket;
        public Thread thread;

        public int _port;
        public string _ipaddr;

        public event RecieveMsg OnReceive;
        public event SocketDisconnect OnDisconnect;
        public event ShowExceptionMsg OnExceptionMsg;

        // ManualResetEvent instances signal completion.
        private static ManualResetEvent connectDone =
            new ManualResetEvent(false);
        private static ManualResetEvent sendDone =
            new ManualResetEvent(false);
        private static ManualResetEvent receiveDone =
            new ManualResetEvent(false);

        public SocketControls(string ipaddr, int port)
        {
            _ipaddr = ipaddr;
            _port = port;
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        /// <summary>
        /// 判断是否连结
        /// </summary>
        /// <returns></returns>
        public Boolean IsConnected()
        {
            return this.clientSocket.Connected;
        }

        public void DisConnect()
        {
            if (clientSocket != null)
            {
                bstop = true;
                if (clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
            }
        }
        public int Connect()
        {
            try
            {
                //clientSocket.Connect(IPAddress.Parse(_ipaddr), _port);
                clientSocket.BeginConnect(IPAddress.Parse(_ipaddr), _port,
                  new AsyncCallback(ConnectCallback), clientSocket);
            }
            catch (Exception ex)
            {
                return 0;
            }
            return 1;
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            Socket client = null;
            try
            {
                // Retrieve the socket from the state object.
                client = ar.AsyncState as Socket;

                // Complete the connection.
                client.EndConnect(ar);

                // Signal that the connection has been made.
                //connectDone.Set();

                thread = new Thread(CheckSocketStatus);
                thread.IsBackground = true;
                thread.Start(_ipaddr);

                // Receive the response from the remote device.
                Receive(clientSocket);


            }
            catch (Exception e)
            {
                thread = new Thread(CheckSocketStatus);
                thread.IsBackground = true;
                thread.Start(_ipaddr);
                ShowMsg(e.ToString());
            }
        }

        public delegate void CheckStatusDel(string ip);
        public event CheckStatusDel CheckStatus;
        private void CheckSocketStatus(object obj)
        {
            string strIP = obj as string;
            byte[] btHeart = { 0X09, 0X00, 0X40, 0X00, 0X00, 0X00, 0X12, 0X00, 0X0A };
            while (true)
            {
                Thread.Sleep(3000);
                if (Send(btHeart) == 0)
                {
                    CheckStatus(strIP);
                    break;
                }
            }
        }

        private int Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                ShowMsg(e.ToString());
                return 0;
            }
            return 1;
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            if (bstop)
            {
                return;
            }
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;
                state.sb.Remove(0, state.sb.Length);
                // Read data from the remote device.
                int bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    //state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    OnReceive(this, state.buffer, bytesRead);

                }
                else
                {
                    receiveDone.Set();
                }

                // Not all data received. Get more.
                if (!bstop)
                {
                    if (bytesRead == 0) //断开连接
                    {
                        state.workSocket.Shutdown(SocketShutdown.Both);
                        state.workSocket.Close();
                        OnDisconnect(this, new EventArgs());
                        return;
                    }
                    if (state.workSocket.Connected == true)
                    {
                        // Get the rest of the data.
                        //尾递归
                        client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            new AsyncCallback(ReceiveCallback), state);
                        receiveDone.Set();
                    }
                    else
                    {
                        //UpdateSocketList(state, DateTime.Now);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ShowMsg(e.ToString());
            }
        }

        public int ClearMessage()
        {
            int iValue = 0;
            byte[] btData = null;
            try
            {
                //btData = new byte[] { 8, 0, 40, 0, 0, 0, 60, 0 };
                btData = new byte[] { 8, 0, 64, 0, 0, 0, 96, 0 };
                iValue = SendDefault(btData);
                return iValue;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public void SendDataToLCD(string value)
        {
            byte[] data = null;
            byte[] datalen = null;
            byte[] CCBHead = null;
            List<byte> listByte = new List<byte>();
            try
            {
                CCBHead = new byte[] { 0, 64, 0, 0, 0, 97, 0 };
                data = Encoding.Default.GetBytes(value);
                datalen = new byte[] { Convert.ToByte(CCBHead.Length + data.Length + 1) };
                //len+CCBHead+data
                listByte.AddRange(datalen);
                listByte.AddRange(CCBHead);
                listByte.AddRange(data);

                SendDefault(listByte.ToArray());
            }
            catch (Exception)
            {

                throw;
            }
        }


        public int SendData(byte[] value, EnumDT4000Interface enumDT4000Interface)
        {
            int iValue = 0;
            byte[] CCBHead = null;
            string writeText = string.Empty;
            List<byte> listSendByte = new List<byte>();
            byte[] sendByte = null;
            byte[] dataLength = null;
            try
            {
                //汉字长度不能正确识别，所以转数组取
                switch (enumDT4000Interface)
                {
                    case EnumDT4000Interface.DO:
                        CCBHead = new byte[] { 0, 64, 0, 0, 0, 9, 0 };
                        break;
                    case EnumDT4000Interface.COM1:
                        CCBHead = new byte[] { 0, 96, 0, 0, 0, 0, 0 };
                        break;
                    case EnumDT4000Interface.COM2:
                        CCBHead = new byte[] { 0, 97, 0, 0, 0, 0, 0 };
                        break;
                    default:
                        CCBHead = new byte[] { 0, 64, 0, 0, 0, 97, 0 };
                        break;
                }
                dataLength = new byte[] { Convert.ToByte(CCBHead.Length + value.Length + 1) };
                listSendByte.AddRange(dataLength);
                listSendByte.AddRange(CCBHead);
                listSendByte.AddRange(value);

                sendByte = listSendByte.ToArray();

                iValue = SendDefault(sendByte);
                return iValue;
            }
            catch (Exception)
            {
                return 0;
            }

        }

        public enum EnumDT4000Interface
        {
            DO = 0,
            COM1 = 1,
            COM2 = 2
        }

        #region Private Function
        private string StrToDefault(string strIn)
        {
            byte[] tobyte = Encoding.Default.GetBytes(strIn);
            string strOut = "";
            for (int i = 0; i < tobyte.Length; i++)
            {
                strOut = strOut + @"\" + Convert.ToString(tobyte[i], 16);
            }
            return strOut;
        }

        private int SendDefault(byte[] sendByte)
        {
            try
            {
                this.clientSocket.BeginSend(sendByte, 0, sendByte.Length, 0,
               new AsyncCallback(ClientSendCallback), clientSocket);
                return 1;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        public int Send(byte[] sendByte)
        {
            try
            {
                this.clientSocket.Send(sendByte, 0, sendByte.Length, SocketFlags.None);
                return 1;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        private void ClientSendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = client.EndSend(ar);
                //ShowMsg("Sent " + bytesSent + " bytes to server.");

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private void ShowMsg(string msg)
        {
            OnExceptionMsg(this, msg);
        }
        #endregion
    }

    public class StateObject
    {
        // Client  socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
    }
}
