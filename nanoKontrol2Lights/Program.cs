﻿using Sanford.Multimedia.Midi;
using System;
using System.Linq;
using System.Threading;
using VoiceMeeterWrapper;

namespace nanoKontrol2Lights
{
    class Program
    {
        private static int GetNanoKontrolInputDevice(string partialDeviceName)
        {
            for (int i = 0; i < InputDevice.DeviceCount; i++)
            {
                var info = InputDevice.GetDeviceCapabilities(i);
                if (info.name.Contains(partialDeviceName))
                    return i;
            }
            throw new Exception($"Cannot find input midi device with '{partialDeviceName}' in the name.");
        }
        private static int GetNanoKontrolOutputDevice(string partialDeviceName)
        {
            for (int i = 0; i < OutputDeviceBase.DeviceCount; i++)
            {
                var info = OutputDeviceBase.GetDeviceCapabilities(i);
                if (info.name.Contains(partialDeviceName))
                    return i;
            }
            throw new Exception($"Cannot find output midi device with '{partialDeviceName}' in the name.");
        }
        private static void SetLight(OutputDevice od, int controlNum, float value)
        {
            od.Send(new ChannelMessage(ChannelCommand.Controller, 0, controlNum, (int)value * 127));
        }
        private static float Scale(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            var zeroToOne = ((value - fromMin) / (fromMax - fromMin));
            var ans =  zeroToOne * (toMax - toMin) + toMin;
            return ans;
        }
        static void Main(string[] args)
        {
            var confTxt = System.IO.File.ReadAllText("nanoKontrol2.txt");
            var config = ConfigParsing.ParseConfig(confTxt);
            var inputMap = config.Bindings.Where(x => (x.Dir & BindingDir.FromBoard) != 0).ToDictionary(x => x.ControlId);

            using (var od = new OutputDevice(GetNanoKontrolOutputDevice(config.DeviceName)))
            using (var id = new InputDevice(GetNanoKontrolInputDevice(config.DeviceName)))
            using (var vb = new VmClient())
            {
                //voicemeeter doesn't have midi bindings for arm/disarm recording. We'll do it ourselves.
                //Note that the voicemeeter UI doesn't update until you start recording something.
                id.ChannelMessageReceived += (ob, e) =>
                {
                    var m = e.Message;
                    if (m.MessageType == MessageType.Channel && m.Command == ChannelCommand.Controller)
                    {
                        if (inputMap.ContainsKey(m.Data1))
                        {
                            var v = inputMap[m.Data1];
                            if(v.ControlToggle && m.Data2 == v.ControlTo)
                            {
                                var current = vb.GetParam(v.VoicemeeterParam);
                                vb.SetParam(v.VoicemeeterParam, v.VmTo - current);
                            }
                            else if(!v.ControlToggle)
                            {
                                var scaledVal = Scale(m.Data2, v.ControlFrom, v.ControlTo, v.VmFrom, v.VmTo);
                                vb.SetParam(v.VoicemeeterParam, scaledVal);
                            }
                        }
                    }
                };
                id.StartRecording();
                vb.OnClose(() =>
                {
                    foreach (var x in config.Bindings.Where(x => (x.Dir & BindingDir.ToBoard) != 0))
                    {
                        od.Send(new ChannelMessage(ChannelCommand.Controller, 0, x.ControlId, (int)x.ControlFrom));
                    }
                });
                while (!Console.KeyAvailable)
                {
                    if (vb.Poll())
                    {
                        foreach (var x in config.Bindings.Where(x => (x.Dir & BindingDir.ToBoard) != 0))
                        {
                            var vmVal = vb.GetParam(x.VoicemeeterParam);
                            var scaled = Scale(vmVal, x.VmFrom, x.VmTo, x.ControlFrom, x.ControlTo);
                            od.Send(new ChannelMessage(ChannelCommand.Controller, 0, x.ControlId, (int)scaled));
                        }
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }
                }
            }
        }
    }
}
