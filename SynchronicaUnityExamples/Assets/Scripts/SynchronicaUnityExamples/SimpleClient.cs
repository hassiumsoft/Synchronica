﻿using FlatBuffers;
using FlatBuffers.Schema;
using Synchronica.Examples.Schema;
using Synchronica.Replayers;
using Synchronica.Schema;
using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Synchronica.Unity.Examples
{
    class SimpleClient 
    {
        private string name;

        private TcpClient tcpClient;

        private NetworkStream networkStream;

        private int objectId;

        private FlatBufferReplayer replayer = new FlatBufferReplayer();

        public SimpleClient(string name, string hostname, int port)
        {
            this.name = name;
            this.tcpClient = new TcpClient();
            this.tcpClient.Connect(hostname, port);
            this.networkStream = this.tcpClient.GetStream();

            Debug.Log(string.Format("Connected to {0}:{1}", hostname, port));

            ThreadPool.QueueUserWorkItem(ReadThread);

            Login();
        }

        public override string ToString()
        {
            return string.Format("{0}-{1}", GetType().Name, this.objectId);
        }

        private void ReadThread(object state)
        {
            var schema = new MessageSchema();
            schema.Register(ServerMessageIds.LoginResponse, LoginResponse.GetRootAsLoginResponse);
            schema.Register(ServerMessageIds.SynchronizeSceneData, SynchronizeSceneData.GetRootAsSynchronizeSceneData);

            var processor = new MessageProcessor(schema);
            processor.Attach((int)ServerMessageIds.LoginResponse, OnLoginResponse);
            processor.Attach((int)ServerMessageIds.SynchronizeSceneData, OnSynchronizeSceneData);

            var buffer = new byte[this.tcpClient.ReceiveBufferSize];

            while (this.networkStream.CanRead)
            {
                var readSize = this.networkStream.Read(buffer, 0, buffer.Length);

                var bytes = new byte[readSize];
                Array.Copy(buffer, bytes, readSize);

                Debug.Log(string.Format("Received {0} bytes", readSize));

                processor.Enqueue(bytes);
                processor.Process();
            }
        }

        private void OnLoginResponse(Message msg)
        {
            var res = (LoginResponse)msg.Body;

            this.objectId = res.ObjectId;

            Debug.Log(string.Format("Login succeeded: {0}", this.objectId));
        }

        private void OnSynchronizeSceneData(Message msg)
        {
            var data = (SynchronizeSceneData)msg.Body;

            Debug.Log(string.Format("Received SynchronizeSceneData: {0} -> {1}", data.StartTime, data.EndTime));
            this.replayer.Replay(data.StartTime, data.EndTime, data);
        }

        public void Login()
        {
            var fbb = new FlatBufferBuilder(1024);

            var oName = fbb.CreateString(this.name);
            var oLogin = LoginRequest.CreateLoginRequest(fbb, oName);
            LoginRequest.FinishLoginRequestBuffer(fbb, oLogin);

            WriteBytes(FlatBufferExtensions.ToProtocolMessage(fbb, ClientMessageIds.LoginRequest));

            Debug.Log("Login");
        }

        public void Input(Command command)
        {
            var time = this.replayer.Scene.ElapsedTime;
            var fbb = new FlatBufferBuilder(1024);

            var oInput = InputRequest.CreateInputRequest(fbb, time, command);
            InputRequest.FinishInputRequestBuffer(fbb, oInput);

            WriteBytes(FlatBufferExtensions.ToProtocolMessage(fbb, ClientMessageIds.InputRequest));

            Debug.Log(string.Format("Input {0} {1}ms", command, time));
        }

        private void WriteBytes(byte[] bytes)
        {
            Debug.Log(string.Format("Send {0} bytes", bytes.Length));

            ThreadPool.QueueUserWorkItem(s => this.networkStream.Write(bytes, 0, bytes.Length));
        }
    }
}
