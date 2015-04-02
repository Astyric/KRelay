﻿using Lib_K_Relay.Networking;
using Lib_K_Relay.Networking.Packets;
using Lib_K_Relay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Lib_K_Relay
{
    public class Proxy
    {
        public int Port = 2050;
        public string ListenAddress = "127.0.0.1";
        public string RemoteAddress = "54.241.208.233"; // USW
        public string Key0 = "311f80691451c71d09a13a2a6e";
        public string Key1 = "72c5583cafb6818995cdd74b80";

        public event Action<Proxy> ProxyListenStarted;
        public event Action<Proxy> ProxyListenStopped;
        public event Action<ClientInstance> ClientConnected;
        public event Action<ClientInstance> ClientDisconnected;
        public event Action<ClientInstance, Packet> ServerPacketRecieved;
        public event Action<ClientInstance, Packet> ClientPacketRecieved;

        private List<Tuple<PacketType, Action<ClientInstance, Packet>>> _packetHooks = 
            new List<Tuple<PacketType, Action<ClientInstance, Packet>>>();

        private TcpListener _localListener = null;

        public void Start()
        {
            Console.WriteLine("[Client Listener] Starting local listener at {0} on port {1}...",
                ListenAddress, Port);

            _localListener = new TcpListener(
                IPAddress.Parse(ListenAddress), Port);

            // Start listening for client connections.
            _localListener.Start();
            _localListener.BeginAcceptTcpClient(LocalConnect, null);

            try
            {
                if (ProxyListenStarted != null)
                    ProxyListenStarted(this);
            } catch (Exception e) { PrintPluginCallbackException("ProxyListenStarted", e); }
        }

        public void Stop()
        {
            if (_localListener != null && !_localListener.Server.Connected)
            {
                Console.WriteLine("[Client Listener] Stopping local listener...");
                _localListener.Stop();
            }

            try
            {
                if (ProxyListenStopped != null)
                    ProxyListenStopped(this);
            } catch (Exception e) { PrintPluginCallbackException("ProxyListenStopped", e); }
        }

        private void LocalConnect(IAsyncResult ar)
        {
            try
            {
                // Finish the accept, and then instantiate a ClientInstance
                // to begin handling IO on that socket and start its own 
                // connection to the server.
                TcpClient client = _localListener.EndAcceptTcpClient(ar);
                ClientInstance ci = new ClientInstance(this, client);

                // Listen for new clients.
                _localListener.BeginAcceptTcpClient(LocalConnect, null);
            }
            catch (ObjectDisposedException ignored) { } // This happens when the proxy stops and the callback fires. We'll ignore it.
            catch (Exception e) 
            {
                Console.WriteLine("[Client Listner] ClientListen failed! Here's the exception report:\n{0}", e.Message);
                Stop();
            }
        }

        public void HookPacket(PacketType type, Action<ClientInstance, Packet> callback)
        {
            if (Serializer.GetPacketId(type) == 255)
                throw new InvalidOperationException("[Plugin Error] A plugin attempted to register callback " +
                                                    callback.GetMethodInfo().ReflectedType + "." + callback.Method.Name +
                                                    " for packet type " + type + " that doesn't have a structure defined.");
            else
                _packetHooks.Add(new Tuple<PacketType, Action<ClientInstance, Packet>>(type, callback));
        }

        public void FireClientConnected(ClientInstance client)
        {
            try
            {
                if (ClientConnected != null)
                    ClientConnected(client);
            } catch (Exception e) { PrintPluginCallbackException("ClientConnected", e); }
        }

        public void FireClientDisconnected(ClientInstance client)
        {
            try
            {
                if (ClientDisconnected != null)
                    ClientDisconnected(client);
            } catch (Exception e) { PrintPluginCallbackException("ClientDisconnected", e); }
        }

        public void FireServerPacket(ClientInstance client, Packet packet)
        {
            try
            {
                // Fire specific hook callbacks if applicable
                foreach (var hook in _packetHooks)
                {
                    if (hook.Item1 == packet.Type && hook.Item2 != null)
                        hook.Item2(client, packet);
                    else if (hook.Item2 == null)
                        _packetHooks.Remove(hook);
                }

                // Fire general server packet callbacks
                if (ServerPacketRecieved != null)
                    ServerPacketRecieved(client, packet);
            } catch (Exception e) { PrintPluginCallbackException("ServerPacket", e); }
        }

        public void FireClientPacket(ClientInstance client, Packet packet)
        {
            try
            {
                // Fire specific hook callbacks if applicable
                foreach (var hook in _packetHooks)
                {
                    if (hook.Item1 == packet.Type && hook.Item2 != null)
                        hook.Item2(client, packet);
                    else if (hook.Item2 == null)
                        _packetHooks.Remove(hook);
                }

                // Fire general client packet callbacks
                if (ClientPacketRecieved != null)
                    ClientPacketRecieved(client, packet);
            } catch (Exception e) { PrintPluginCallbackException("ClientPacket", e); }
        }

        private void PrintPluginCallbackException(string caller, Exception e)
        {
            MethodBase site = e.TargetSite;
            string methodName = site == null ? "<null method reference>" : site.Name;
            string className = site == null ? "" : site.ReflectedType.Name;

            Console.WriteLine("[Plugin Error] An exception was thrown\nwithin a {0} callback\nat {1}\nHere's the exception report:\n{2}",
                caller, className + "." + methodName, e.Message);
        }
    }
}
