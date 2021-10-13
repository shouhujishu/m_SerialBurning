using RJCP.IO.Ports;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace m_SerialBurning
{
    class Program
    {
       static SerialPortStream Port1;
        static UdpClient Udp;
        static bool WifiSerial=false;
        static IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        static string ComNmae = "";
        static string FirPath = "";
        static int UdpClientPort = 8808;
        static int WmSleep = 0;//写出延迟
        static void Main(string[] args)
        {

          
            Console.WriteLine("            守护-M芯片烧录工具(03T)");
           
            if (args.Length==0)
            {
                Console.WriteLine("=======可用端口=======");
                foreach (var item in SerialPortStream.GetPortNames())
                {
                    Console.WriteLine("  "+item);
                }
          
            }
            else
            {
               
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i]== "-w")
                    {
                        WifiSerial = true;
                    }
                    else if (args[i] == "-p")
                    {
                        i++;
                        UdpClientPort = int.Parse(args[i]);
                    }
                    else if (args[i] == "-m")
                    {
                        i++;
                        ComNmae = args[i];
                    }
                    else if (args[i] == "-f")
                    {
                        i++;
                        FirPath = args[i];
                    }
                    else if (args[i] == "-wm")
                    {
                        i++;
                        WmSleep = int.Parse(args[i]);
                    }
                }

 
                if (WifiSerial)
                {
                    Console.WriteLine("=======无线烧录模式(非稳定烧录)=======");
                    Console.WriteLine("开始烧录时如果超2分钟没有进度说明失败");
                    Console.WriteLine("UDP端口:"+ UdpClientPort.ToString());
                   
                }
                else
                {
                    if (!SerialPortStream.GetPortNames().Contains(ComNmae))
                    {
                        Console.WriteLine("端口: " + ComNmae + " 不存在！");
                        goto end;
                    }
                }

               
                if (!File.Exists(FirPath))
                {
                    Console.WriteLine("固件文件不存在("+ FirPath + ")");
                    goto end;
                }

                try
                {
                    if (!WifiSerial)
                    {
                        if (Environment.OSVersion.Platform == PlatformID.Unix)
                        {
                            RunCommand("export  LD_LIBRARY_PATH=" + AppContext.BaseDirectory + ":LD_LIBRARY_PATH");
                            Thread.Sleep(300);
                        }
                    }
                    List<byte[]> Bins=  LoadBin(FirPath);
                    if (WifiSerial)
                    {
                        Udp = new UdpClient(UdpClientPort);
                    }
                    else
                    {
                        Port1 = new SerialPortStream(ComNmae, 921600, 8, Parity.None, StopBits.One);
                        Port1.ReadBufferSize = 1030;
                        Port1.WriteBufferSize = 1030;
                        Console.WriteLine("开启串口:" + ComNmae);
                        int Num = 0;
                        while (!OpenSerial())
                        {
                            Num++;
                            if (Num > 6)
                            {
                                Console.WriteLine("串口打开错误次数过多,已停止");
                                goto end;
                            }
                            Thread.Sleep(1000 * 2);
                        }
                    }
                  
                    if (WaitingEnter())
                    {
                        WiteBins(Bins);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("烧录异常: "+ex.Message);

                }
              

            }
            end:
            if (Port1!=null)
            {
                if (Port1.IsOpen)
                {
                    Port1.Close();
                    Port1.Dispose();
                }
               
            }
            if (Udp!=null)
            {
                Udp.Close();
            }
            Console.WriteLine("\r\n按任意键退出");
            Console.ReadLine();
        }

        static byte[] Serial_Read() {
            byte[] Data = new byte[0];
            if (WifiSerial)
            {
                if (Udp.Available>0)
                {
                    Data = Udp.Receive(ref RemoteIpEndPoint);
                }
            }
            else
            {
                if (Port1.BytesToRead > 0)
                {
                    byte[] readBuf = new byte[100];
                    int ReadLen = Port1.Read(readBuf, 0, 100);
                    if (ReadLen>0)
                    {
                        Data = new byte[ReadLen];
                        Array.Copy(readBuf, Data, ReadLen);
                    }
                }
            }
            return Data;
        }
        static void Udp_Empty()
        {
            if (WifiSerial)
            {
                while (Udp.Available > 0)
                {
                    Udp.Receive(ref RemoteIpEndPoint);
                    Thread.Sleep(1);
                }
            }
        
    
        }

        static void Serial_Write(byte[] Data, int len)
        {
            if (WifiSerial)
            {

                Udp.Client.SendTo(Data, len, SocketFlags.None, RemoteIpEndPoint);
            }
            else
            {
                Port1.Write(Data, 0, len);
            }
        }

        static bool OpenSerial() {
            try
            {
                Port1.Open();
                return true;
            }
            catch (Exception ex)
            {

                Port1.Close();
                Console.WriteLine("串口打开失败,重试中"+ ex.Message);
            }
            return false;
        }
        static void RunCommand(string command) {
            using (System.Diagnostics.Process proc = new System.Diagnostics.Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();
                proc.WaitForExit();
            }
          
        }
        /// <summary>
        /// 等待设备进入
        /// </summary>
        /// <returns></returns>
        static bool WaitingEnter() {
            DateTime _satrt = DateTime.Now;
            byte[] readBuf;
            Console.WriteLine("等待设备进入BOOT,超时时间60秒");
            while ((DateTime.Now- _satrt).TotalSeconds<120)
            {
                if (WifiSerial)
                {
                    Thread.Sleep(10);
                }
                else
                {
                    Serial_Write(new byte[1] { 50 }, 1);
                     Thread.Sleep(10);
                }
                readBuf= Serial_Read();
                if (readBuf.Length>0)
                {
                    if (readBuf.Length>=1 && readBuf[0] == 67 )
                    {
                        // && readBuf[1] == 67
                        //ReadLen == 5 &&
                        //&& readBuf[2] == 67 && readBuf[3] == 67 && readBuf[4] == 67
                        Console.WriteLine("BOOT 下载模式准备完毕");
                        return true;
                    }
                    else if (IndexArray(readBuf,new byte[] { 104, 117, 109, 98, 105, 114, 100, 45, 77, 32, 98, 111, 111, 116, 108, 111, 97, 100, 101, 114 } )) //humbird-M bootloader
                    {
                        Console.WriteLine("BOOT 进入");
                    }
                    else if (IndexArray(readBuf,new byte[] {  103, 111, 32, 116, 111, 32, 100, 111, 119, 110, 108, 111, 97, 100, 32, 109, 111, 100})) //go to download mode
                    {
                        Console.WriteLine("BOOT 跳转下载模式中");
                    }

                }

            }
            throw new Exception("等待设备进入BOOT超时");
        
        }
        static bool WiteBins(List<byte[]> Bins)
        {
            Udp_Empty();
            int BasicProgress =  (int)Math.Ceiling((double)(Bins.Count - 1) / 100);
            Console.WriteLine("烧录中...");
            byte[] readBuf;
            int Progress = 0;
            for (int i = 0; i < Bins.Count; i++)
            {

                Serial_Write(Bins[i], Bins[i].Length);
           

                readBuf = Serial_Read();
                while (readBuf.Length == 0)
                {
                    if (!WifiSerial)
                    {
                        Thread.Sleep(5);
                    }
                    readBuf = Serial_Read();
                    if (readBuf.Length>0 && readBuf[0] == 6)
                    {
                        break;
                    }
                   
                     if(readBuf.Length>0)
                    {
                        foreach (var item in readBuf)
                        {
                            System.Diagnostics.Debug.Write((int)item);
                            System.Diagnostics.Debug.Write(" ");
                        }
                        System.Diagnostics.Debug.WriteLine("");
                        readBuf = new byte[0];
                    }
                     if (readBuf.Length == 1 && readBuf[0] == 0)
                    {
                        break;
                    }
                }

                if (readBuf.Length >0 && readBuf[0] == 6)
                {
                    if ((int)i / BasicProgress > Progress)
                    {
                        Progress = (int)i / BasicProgress;
                        Console.Write("\r烧录..." + Progress.ToString() + "%");
                    }
                   
                }
                else
                {
                    throw new Exception("烧录失败,重试或用官方烧录工具重试");
                }
                Thread.Sleep(WmSleep);
            }

            if (Progress!=100)
            {
                Progress = 100;
                Console.WriteLine("\r烧录..." + Progress.ToString() + "%");
            }
            //烧录结束
            Serial_Write(new byte[1] { 4 },1);
            Thread.Sleep(10);
            if (!WifiSerial)
            {
                readBuf = Serial_Read();
                if (readBuf.Length > 0 && IndexArray(readBuf, new byte[] { 116, 114, 97, 110, 115, 102, 101, 114, 32, 100, 111, 110, 101 })) //transfer done:1225728
                {
                    Console.WriteLine("固件烧录成功.");
                    return true;
                }
                else
                {
                    Console.WriteLine("固件烧录结尾异常.");
                }
            }
            else
            {
                Console.WriteLine("无线烧录结束");
                return true;
            }
           
            return false;


        }

            /// <summary>
            /// 加载分割固件
            /// </summary>
        static  List<byte[]> LoadBin(string FilePath) {
            List<byte[]> Bins = new List<byte[]>();
         //   FileStream R = new FileStream(FilePath, FileMode.);
            MemoryStream m = new MemoryStream(File.ReadAllBytes(FilePath));
            BufferedStream R = new BufferedStream(m);
          
            //固件分割索引
            int index = 1;
            while (R.Position != R.Length)
            {

                byte[] data = new byte[1024];
                byte[] CRCdata = new byte[data.Length+5];
               
                int len = R.Read(data, 0, data.Length);
                if (data.Length - len > 0)
                {//填充数据达到
                    for (int i = len; i < data.Length; i++)
                    {
                        data[i] = 26;
                    }
                }

                if (index > 255)
                {
                    index = 0;
                }
                CRCdata[0] = 2;
                CRCdata[1] = (byte)index;
                CRCdata[2] = (byte)(255 - index);
                Array.Copy(data, 0, CRCdata, 3, data.Length);
                Array.Copy(BitConverter.GetBytes(Cal_crc16(data)).Reverse().ToArray(), 0, CRCdata, CRCdata.Length-2, 2);
                Bins.Add(CRCdata);
                index++;
            }
            R.Close();
            m.Close();
            return Bins;
        }

        public static bool IndexArray(byte[] src ,byte[] des) {
            int i = 0;
            foreach (byte item in des)
            {
                i = Array.IndexOf(src, item, i);
                if (i < 0)
                {
                    return false;
                }

            }
            return true;

        }
        public static UInt16 Cal_crc16(byte[] data)
        {

            UInt32 i = 0;
            UInt16 crc = 0;
            for (i = 0; i < data.Length; i++)
            {
                crc = UpdateCRC16(crc, data[i]);
            }
            crc = UpdateCRC16(crc, 0);
            crc = UpdateCRC16(crc, 0);

            return (UInt16)(crc);
        }
        public static UInt16 UpdateCRC16(UInt16 crcIn, byte bytee)
        {
            UInt32 crc = crcIn;
            UInt32 ins = (UInt32)bytee | 0x100;

            do
            {
                crc <<= 1;
                ins <<= 1;
                if ((ins & 0x100) == 0x100)
                {
                    ++crc;
                }
                if ((crc & 0x10000) == 0x10000)
                {
                    crc ^= 0x1021;
                }
            }
            while (!((ins & 0x10000) == 0x10000));
            return (UInt16)crc;
        }
    }
}
