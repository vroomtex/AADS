using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        
        Dictionary<long, MyTuple<Vector3D, Vector3D, double>> NETWORK_CONTACTS = new Dictionary<long, MyTuple<Vector3D, Vector3D, double>>();
        Dictionary<long, MyTuple<Vector3D, Vector3D>> VN_TO_LN_BUFFER = new Dictionary<long, MyTuple<Vector3D, Vector3D>>();
        Dictionary<long, MyTuple<Vector3D, Vector3D>> LN_TO_VN_BUFFER = new Dictionary<long, MyTuple<Vector3D, Vector3D>>();
        Dictionary<long, int> cStreamRequestDict = new Dictionary<long, int>();

        StringBuilder _setupStringbuilder = new StringBuilder();
        StringBuilder _echoBuilder = new StringBuilder();
        IMyTerminalBlock _reference = null;
        IMyBroadcastListener _vnListener;
        IMyBroadcastListener _cStreamRequester;

        ImmutableArray<MyTuple<byte, long, Vector3D, double>>.Builder _messageBuilder = ImmutableArray.CreateBuilder<MyTuple<byte, long, Vector3D, double>>();
        ImmutableArray<MyTuple<Vector3D, Vector3D, long>>.Builder _contactBuilder = ImmutableArray.CreateBuilder<MyTuple<Vector3D, Vector3D, long>>();
        Program()
        {
            _vnListener = IGC.RegisterBroadcastListener("IGC_RGSTR_PKT");
            _cStreamRequester = IGC.RegisterBroadcastListener("IGC_CST_REQ");
        }

        void updateRadarContact(Vector3D targetPosition, Vector3D targetVelocity, long key)
        {
            NETWORK_CONTACTS[key] = new MyTuple<Vector3D, Vector3D, double>(targetPosition, targetVelocity, 0.0);
        }
        void writeVNtoLN()
        {
            while (_vnListener.HasPendingMessage)
            {
                MyIGCMessage message = _vnListener.AcceptMessage();
                if (message.Source != Me.GetId()) // Message is not from itself.
                {
                    var messageData = (MyTuple<long, Vector3D, Vector3D, double>)message.Data;
                    // Create/Update the contact. We update the contact because 
                    VN_TO_LN_BUFFER[messageData.Item1] = new MyTuple<Vector3D, Vector3D>(messageData.Item2, messageData.Item3);
                }

            }
            // Once Buffer is done.
            foreach (var contact in VN_TO_LN_BUFFER)
            {
                updateRadarContact(contact.Value.Item1, contact.Value.Item2, contact.Key);
            }
            VN_TO_LN_BUFFER.Clear();
            // Clear buffer to save memory.
        }
        bool memoryStatus()
        {
            while (_cStreamRequester.HasPendingMessage)
            {
                MyIGCMessage message = _cStreamRequester.AcceptMessage();
                if (message.Source != Me.GetId()) // Message is not from itself.
                {
                    long messageData = (long)message.Data;
                    return true;
                }
            }
            return false;
        }
        bool cStreamDetection(long key)
        {
            return IGC.RegisterBroadcastListener(key.ToString()).HasPendingMessage;
        }
        void PrintDetailedInfo()
        {
            _echoBuilder.AppendLine($"Ruby-22A Fire Control System");
            _echoBuilder.AppendLine($"\n Authorized Users Only\n");
            var keys = new List<long>(NETWORK_CONTACTS.Keys);
            foreach (var key in keys)
            {
                var localContactData = NETWORK_CONTACTS[key]; // MyTuple<Vector3D, Vector3D, double>
                _echoBuilder.AppendLine($"\n Contact: {key}, cStreamActive: {cStreamDetection(key)}  cStreamRequestStatus: {memoryStatus()}\n");
            }
            _echoBuilder.AppendLine($"\n End of Contact List\n");
            Echo(_echoBuilder.ToString());
            _echoBuilder.Clear();
        }

        void Main(string arg, UpdateType updateType)
        {
            writeVNtoLN();
            PrintDetailedInfo();
            long id = 3843;
            double timLock = 0;
            MyTuple<long,Vector3D, Vector3D, double> Data = new MyTuple<long,Vector3D, Vector3D, double>(id,new Vector3D(0,0,0), new Vector3D(0, 0, 0), timLock);
         //   IGC.SendBroadcastMessage("IGC_RGSTR_PKT", Data);
        }
    }
}
