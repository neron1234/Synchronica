﻿using FlatBuffers;
using FlatBuffers.Schema;
using NLog;
using Synchronica.Examples.Scene;
using Synchronica.Examples.Schema;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Synchronica.Examples.Server
{
    class SimpleClient
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private string name = "NoName";

        private int actorId;

        private TcpClient tcpClient;

        private NetworkStream networkStream;

        private SimpleScene scene;

        public SimpleClient(TcpClient tcpClient, SimpleScene scene)
        {
            this.tcpClient = tcpClient;
            this.networkStream = tcpClient.GetStream();
            this.scene = scene;

            logger.Info("Connected from {0}", tcpClient.Client.RemoteEndPoint);

            ThreadPool.QueueUserWorkItem(ReadThread);
        }

        public override string ToString()
        {
            return string.Format("{0}-{1}", GetType().Name, this.name);
        }

        private void ReadThread(object state)
        {
            var schema = new MessageSchema();
            schema.Register(ClientMessageIds.LoginRequest, LoginRequest.GetRootAsLoginRequest);
            schema.Register(ClientMessageIds.InputRequest, InputRequest.GetRootAsInputRequest);

            var processor = new MessageProcessor(schema);
            processor.Attach((int)ClientMessageIds.LoginRequest, OnLoginRequest);
            processor.Attach((int)ClientMessageIds.InputRequest, OnInputRequest);

            var buffer = new byte[this.tcpClient.ReceiveBufferSize];

            while (this.networkStream.CanRead)
            {
                int readSize;
                try
                {
                    readSize = this.networkStream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    readSize = 0;
                }

                if (readSize == 0)
                {
                    logger.Info("Disconnected");
                    break;
                }

                var bytes = new byte[readSize];
                Array.Copy(buffer, bytes, readSize);

                logger.Info("Received {0} bytes", readSize);

                processor.Enqueue(bytes);
                processor.Process();
            }
        }

        private void OnLoginRequest(Message msg)
        {
            var req = (LoginRequest)msg.Body;

            this.name = req.Name;
            this.actorId = this.scene.AllocateCube(this.name);

            if (this.actorId > 0)
            {
                logger.Info("Login succeeded: {0}, ObjectId: {1}", this.name, this.actorId);

                var fbb = new FlatBufferBuilder(1024);
                var oRes = LoginResponse.CreateLoginResponse(fbb, this.actorId);
                LoginResponse.FinishLoginResponseBuffer(fbb, oRes);

                WriteBytes(FlatBufferExtensions.ToProtocolMessage(fbb, ServerMessageIds.LoginResponse));

                foreach (var bytes in this.scene.GetSceneData())
                    WriteBytes(bytes);
            }
            else
            {
                logger.Info("Login failed.");
            }
        }

        private void OnInputRequest(Message msg)
        {
            var req = (InputRequest)msg.Body;

            this.scene.AddInput(this.actorId, req.Milliseconds, req.Command);
        }

        public void WriteBytes(byte[] bytes)
        {
            ThreadPool.QueueUserWorkItem(s => this.networkStream.Write(bytes, 0, bytes.Length));
        }

        public bool LoginSucceeded
        {
            get { return this.actorId > 0; }
        }
    }
}
