﻿using System;

using System.Net.Sockets;
using System.Net;
using System.IO;

using System.IO.Compression;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;

namespace RemoteDesktop.Android.Core
{
	struct ReceiveState
	{
        public int size;
        public int bytesRead;
    }

    // DEBUG: dummy for avoid erros due to Xamarin does not have some classes
    [Serializable()]
    public class PixelFormatXama
    {
        public static PixelFormatXama Format24bppRgb = new PixelFormatXama(0);
        public static PixelFormatXama Format16bppRgb565 = new PixelFormatXama(1);

        private int format = -1;
        public PixelFormatXama(int pixcel_format) {
            format = pixcel_format;
        }
        public int getFormat()
        {
            return format;
        }
    }

    // DEBUG: dummy for avoid erros due to Xamarin does not have some classes
    public class BitmapXama
    {
        public PixelFormatXama PixelFormat = new PixelFormatXama(-1);
        private byte[] buffer = null;

        //public BitmapXama() { }
        public BitmapXama(byte[] buf)
        {
            buffer = buf;
        }

        //public void UnlockBits(BitmapData locked) { }
        public byte[] getInternalBuffer()
        {
            return buffer;
        }

        public int Height = 0;
        public int Width = 0;
    }

    //[Serializable()]
    public enum MetaDataTypes
	{
		None,
		UpdateSettings,
		StartCapture,
		PauseCapture,
		ResumeCapture,
		UpdateMouse,
		UpdateKeyboard,
		ImageData
	}


    //[Serializable()]
    [StructLayout(LayoutKind.Sequential)]
	public struct MetaData
	{
		public MetaDataTypes type;
		public bool compressed;
		public int dataSize, imageDataSize;
        public short width, height, screenWidth, screenHeight;//, screenIndex;
		public float resolutionScale;
		public float targetFPS;

		public short mouseX, mouseY;
        public MouseInteractionType mouseInteractionType;

		//public sbyte mouseScroll;
		//public byte mouseButtonPressed, keyCode, specialKeyCode;
	}

	public class DataSocket : IDisposable
	{
		public delegate void ConnectionCallbackMethod();
		public delegate void DisconnectedCallbackMethod();
		public delegate void ConnectionFailedCallbackMethod(string error);
		public delegate void DataRecievedCallbackMethod(byte[] data, int dataSize, int offset);
		public delegate void StartDataRecievedCallbackMethod(MetaData metaData);
		public delegate void EndDataRecievedCallbackMethod();

		public event ConnectionCallbackMethod ConnectedCallback;
		public event DisconnectedCallbackMethod DisconnectedCallback;
		public event ConnectionFailedCallbackMethod ConnectionFailedCallback;
		public event DataRecievedCallbackMethod DataRecievedCallback;
		public event StartDataRecievedCallbackMethod StartDataRecievedCallback;
		public event EndDataRecievedCallbackMethod EndDataRecievedCallback;

		private NetworkTypes type;
		private Socket listenSocket, socket;
		private bool isDisposed = false, disconnected = false;
		private Timer disconnectionTimer;

		private byte[] receiveBuffer, sendBuffer, metaDataBuffer;
		private int metaDataBufferRead;
		private readonly int metaDataSize;
		private MetaData metaData;
		private MemoryStream compressedStream;
        private int frameNumber = 0;

        private const int BUF_SIZE = 2048;

		public DataSocket(NetworkTypes type)
		{
			this.type = type;

			receiveBuffer = new byte[BUF_SIZE];
			sendBuffer = new byte[BUF_SIZE];

            //BinaryFormatter bf = new BinaryFormatter();
            //MemoryStream ms = new MemoryStream();
            //bf.Serialize(ms, new MetaData()); // for get Binary Seriazed data size
            //metaDataSize = (int) ms.Length;
            metaDataSize = Marshal.SizeOf(new MetaData());

            Console.WriteLine("Serialized MetaData Class object binary size is berow");
            Console.WriteLine(metaDataSize);
            Console.WriteLine(type);
			metaDataBuffer = new byte[1024];
		}

		public void Dispose()
		{
			disconnected = true;

			// dispose timer
			if (disconnectionTimer != null)
			{
				disconnectionTimer.Dispose();
				disconnectionTimer = null;
			}

			lock (this)
			{
				isDisposed = true;

				// dispose listener socket
				if (listenSocket != null)
				{
					try
					{
						listenSocket.Shutdown(SocketShutdown.Both);
					}
					catch { }

					listenSocket.Close();
					listenSocket = null;
				}

				// dispose normal socket
				if (socket != null)
				{
					try
					{
						socket.Shutdown(SocketShutdown.Both);
					}
					catch { }

					try
					{
						if (IsConnected(socket)) socket.Disconnect(false);
					}
					catch { }

					socket.Close();
					socket.Dispose();
					socket = null;
				}

				// dispose compression stream
				if (compressedStream != null)
				{
					compressedStream.Dispose();
					compressedStream = null;
				}

				// null types
				receiveBuffer = null;
				sendBuffer = null;
				metaDataBuffer = null;
			}
		}

		private void StartDisconnectionTimer()
		{
			disconnectionTimer = new Timer(timerTic, null, 100, 1000);
		}

		private void timerTic(object state)
		{
			lock (this)
			{
				if (isDisposed) return;

				if (!IsConnected())
				{
					if (disconnectionTimer != null)
					{
						disconnectionTimer.Dispose();
						disconnectionTimer = null;
					}

					metaDataBufferRead = 0;
					Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
					Array.Clear(sendBuffer, 0, sendBuffer.Length);
					Array.Clear(metaDataBuffer, 0, metaDataBuffer.Length);

					FireDisconnectedCallback();
				}
			}
		}

		public void Listen(IPAddress ipAddress, int port)
		{
			if (type != NetworkTypes.Server) throw new Exception("Only allowed for server!");

			lock (this)
			{
				listenSocket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				listenSocket.Bind(new IPEndPoint(ipAddress, port));
				listenSocket.Listen(1);
				listenSocket.BeginAccept(ConnectionEstablishedCallback, null);
			}
		}

		public void ReListen()
		{
			if (type != NetworkTypes.Server) throw new Exception("Only allowed for server!");
			lock (this)
			{
				if (listenSocket == null) throw new Exception("Must call listen before this method.");
				if (IsConnected()) throw new Exception("Socket already connected");
				listenSocket.BeginAccept(ConnectionEstablishedCallback, null);
			}
		}

		public void Connect(IPAddress ipAddress, int port)
		{
			lock (this) Connect(new IPEndPoint(ipAddress, port));
		}

		public void Connect(IPEndPoint endPoint)
		{
			lock (this)
			{
				socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				socket.BeginConnect(endPoint, ConnectionEstablishedCallback, null);
			}
		}

		private void ConnectionEstablishedCallback(IAsyncResult ar)
		{
			lock (this)
			{
				if (isDisposed) return;
				disconnected = false;
                frameNumber = 0;

				// start diconnection timer
				StartDisconnectionTimer();

				// connect
				if (type == NetworkTypes.Server)
				{
					try
					{
						socket = listenSocket.EndAccept(ar);
					}
					catch (SocketException e)
					{
						string error = string.Format("socket.EndConnect failed: {0}\n{1}", e.SocketErrorCode, e.Message);
						FireConnectionFailedCallback(error);
						Console.WriteLine(error);
						disconnected = true;
						return;
					}
					catch (Exception e)
					{
						string error = "socket.EndConnect failed: " + e.Message;
						FireConnectionFailedCallback(error);
						Console.WriteLine(error);
						disconnected = true;
						return;
					}

					FireConnectedCallback();
				}
				else
				{
					try
					{
						socket.EndConnect(ar);
					}
					catch (SocketException e)
					{
						string error = string.Format("socket.EndConnect failed: {0}\n{1}", e.SocketErrorCode, e.Message);
						FireConnectionFailedCallback(error);
						Console.WriteLine(error);
						disconnected = true;
						return;
					}
					catch (Exception e)
					{
						string error = "socket.EndConnect failed: " + e.Message;
						FireConnectionFailedCallback(error);
						Console.WriteLine(error);
						disconnected = true;
						return;
					}

					FireConnectedCallback();
				}

				// begin recieve listen
				try
				{
					socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, new ReceiveState());
				}
				catch (Exception e)
				{
					Console.WriteLine("Failed to BeginReceive SocketConnection: " + e.Message);
					disconnected = true;
					return;
				}
			}
		}

        private void debugPrinbByteArray(byte[] buf, int span)
        {
            if (!GlobalConfiguration.isStdOutOff)
            {
                string debugStr = "contents of buffer " + span.ToString() + " elem span :";
                for (int i = 0; i < buf.Length; i += span)
                {
                    debugStr += buf[i].ToString() + ", ";
                }
                Console.WriteLine(debugStr);
            }
        }

        private void debugPrintByteArray4ElemSpan(byte[] buf)
        {
            debugPrinbByteArray(buf, 4);
        }

		private void ReceiveBufferShiftDown(int atIndex)
		{
            Console.WriteLine("call ReceiveBufferShiftDown func. atIndex: " + atIndex.ToString());
            //debugPrintByteArray4ElemSpan(receiveBuffer);
			for (int i = 0, i2 = atIndex; i2 < receiveBuffer.Length; ++i, ++i2)
			{
				receiveBuffer[i] = receiveBuffer[i2];
				receiveBuffer[i2] = 0;
			}
		}

		private void RecieveDataCallback(IAsyncResult ar)
		{
            Console.WriteLine("ReceiveDataCallback on DataSockt");
            Console.WriteLine((ReceiveState)ar.AsyncState);

            try
            {
                lock (this)
                {
                    // validate socket
                    if (isDisposed || socket == null || !socket.Connected) return;

                    // handle failed reads
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = socket.EndReceive(ar); // this retuened larger than buffer size? (it means total read byte size?) => maybe No
                        Console.WriteLine("read data size got from socketEndReceive(ar): " + bytesRead.ToString());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        disconnected = true;
                    }

                    // write data to stream
                    var state = (ReceiveState)ar.AsyncState;
                    Console.WriteLine("top of RecieveDataCallback state.size = " + state.size.ToString());
                    Console.WriteLine("top of RecieveDataCallback state.bytesRead = " + state.bytesRead.ToString());
                    Console.WriteLine("top of RecieveDataCallback bytesRead = " + bytesRead.ToString());
                    Console.WriteLine("top of RecieveDataCallback metaDataBufferRead = " + metaDataBufferRead.ToString());

                    if (bytesRead > 0)
                    {
                        EXTRA_STREAM:;
                        Console.WriteLine("pass EXTRA_STREAM at DataSocket");
                        int overflow = 0;

                        // read meta data
                        if (metaDataBufferRead < metaDataSize)
                        {
                            int count = Math.Min(metaDataSize - metaDataBufferRead, bytesRead);
                            Array.Copy(receiveBuffer, 0, metaDataBuffer, metaDataBufferRead, count);
                            metaDataBufferRead += count;

                            // DEBUG: if EndReceive func do not return total read bytes, calc sum code may be needed!!!
                            //if (bytesRead < metaDataSize)
                            if (metaDataBufferRead < metaDataSize)
                            {
                                Console.WriteLine("RecieveDataCallback: metaDataBufferRead < metaDataSize: " + metaDataBufferRead.ToString() + " ,  " + metaDataSize.ToString());
                                try
                                {
                                    socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, state);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex);
                                }
                                return;
                            }
                            else
                            {
                                ReceiveBufferShiftDown(count);
                                bytesRead -= count;
                                overflow = bytesRead; // overflow and current bytesRead means bitmap data already read (if value > 0)

                                //BinaryFormatter bf = new BinaryFormatter();
                                //try
                                //{
                                //    metaData = (MetaData)bf.Deserialize(new MemoryStream(metaDataBuffer));
                                try
                                {
                                    IntPtr ptr = Marshal.AllocHGlobal(metaDataSize);
                                    Marshal.Copy(metaDataBuffer, 0, ptr, metaDataSize);
                                    metaData = (MetaData)Marshal.PtrToStructure(ptr, typeof(MetaData));
                                    Marshal.FreeHGlobal(ptr);
                                }
                                catch (Exception ex)
                                {
                                    // print information for debug
                                    debugPrinbByteArray(metaDataBuffer, 1);
                                    debugPrinbByteArray(receiveBuffer, 1);
                                    Console.WriteLine("state.size = " + state.size.ToString());
                                    Console.WriteLine("state.bytesRead = " + state.bytesRead.ToString());
                                    Console.WriteLine("bytesRead = " + bytesRead.ToString());
                                    Console.WriteLine("metaDataBufferRead = " + metaDataBufferRead.ToString());
                                    Console.WriteLine("count = " + count.ToString());
                                    Console.WriteLine(metaData);
                                    Console.Out.Flush();

                                    throw ex;
                                }

                                if (metaData.dataSize == 0) throw new Exception("Invalid data size");
                                Console.WriteLine("read MetaData at DataSocket success!");

                                // fire start callback
                                FireStartDataRecievedCallback(metaData);

                                // check if message type (if so finish and exit)
                                if (metaData.dataSize == -1)
                                {
                                    FireEndDataRecievedCallback();
                                    metaDataBufferRead = 0;
                                    state = new ReceiveState();

                                    Console.WriteLine("metaData.dataSize == -1. FireEndDataRecievedCallback state.bytesRead=" + state.bytesRead.ToString() + " state.size=" + state.size.ToString());
                                    FireEndDataRecievedCallback();
                                    Console.WriteLine("metaData.dataSize == -1. call socket.BeginReceive func to read next frame because finished read all data of current frame. metaDataBufferRead: " + metaDataBufferRead.ToString());
                                    metaDataBufferRead = 0;
                                    try
                                    {
                                        socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, new ReceiveState());
                                    }
                                    catch (Exception ex)
                                    {
                                        throw ex;
                                    }
                                    return;
                                }
                                else // server write data after MetaData object and read the data
                                {
                                    Console.WriteLine("set state size after deserialize MetaData:" + metaData.dataSize.ToString());
                                    state.size = metaData.dataSize; // bitmap data size
                                                                    //state.bytesRead = overflow;
                                }

                                Console.WriteLine("value of state at end of MetaData process block: " + state.size.ToString() + ", " + state.bytesRead.ToString());
                                if (overflow > 0) // this means already read but not used data left on receive buffer
                                {
                                    Console.WriteLine("goto EXTRA_STREAM on MetaData process block: this means already read but not used data left on receive buffer");
                                    goto EXTRA_STREAM;
                                }
                                else // go to read yet not received data (bitmap data)
                                {
                                    Console.WriteLine("go to read yet not received data (bitmap data)");
                                    try
                                    {
                                        socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, state);
                                    }
                                    catch (Exception ex)
                                    {
                                        throw ex;
                                    }
                                    return;
                                }
                            }
                        } // not through code from inner of this block to below!!!

                        // --- after MetaData object is read ---

                        Console.WriteLine("value of state *AFTER* end of MetaData process block: " + state.size.ToString() + ", " + state.bytesRead.ToString());

                        // read data chunk
                        int offset = state.bytesRead;
                        Console.WriteLine("update state.bytesRead: current state.bytesRead = " + state.bytesRead.ToString() + " bytesRead = " + bytesRead.ToString() + " updated state.bytesRead = " + (state.bytesRead + bytesRead).ToString());
                        state.bytesRead += bytesRead;
                        overflow = Math.Max(state.bytesRead - state.size, 0); // overflow > 0 means already read next frame data
                        state.bytesRead = Math.Min(state.bytesRead, state.size);
                        int byteCount = bytesRead - overflow; // calc data size of current frame on receiveBuffer
                        FireDataRecievedCallback(receiveBuffer, byteCount, offset);
                        Console.WriteLine("byteCount which means data size of current frame on receiveBuffer: " + byteCount.ToString());
                        Console.WriteLine("overflow which already data size of next frame on receiveBuffer: " + overflow.ToString());

                        //if (state.bytesRead != state.size) // did not read all data of current frame yet
                        if (state.bytesRead < state.size) // did not read all data of current frame yet
                        {
                            Console.WriteLine("call socket.BeginReceive func to read left bitmap data of current frame");
                            try
                            {
                                socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, state);
                            }
                            catch (Exception ex)
                            {
                                throw ex;
                            }
                            return;
                        }
                        else // already read all data of current frame
                        {
                            Console.WriteLine("FireEndDataRecievedCallback state.bytesRead=" + state.bytesRead.ToString() + " state.size=" + state.size.ToString());
                            FireEndDataRecievedCallback();
                        }


                        if (overflow > 0) // process remaining data (already read next frame data)
                        {
                            state = new ReceiveState();
                            ReceiveBufferShiftDown(bytesRead - overflow); // remove current frame data
                            bytesRead = overflow;
                            metaDataBufferRead = 0;
                            Console.WriteLine("goto EXTRA_STREAM *AFTER* MetaData process block: this means already read but not used data left on receive buffer (size is BytesRead, overflow)");
                            goto EXTRA_STREAM;
                        }
                        else // finish read all data of current frame. then start wait data arrive of next frame
                        {
                            Console.WriteLine("call socket.BeginReceive func to read next frame because finished read all data of current frame. metaDataBufferRead: " + metaDataBufferRead.ToString());
                            metaDataBufferRead = 0;
                            try
                            {
                                socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, RecieveDataCallback, new ReceiveState());
                            }
                            catch (Exception ex)
                            {
                                throw ex;
                            }
                            return;
                        }
                    }
                    else // read request to socket failed (some failue should occured) 
                    {
                        Console.WriteLine("read request to socket failed (some failue should occured) ");
                        disconnected = true;
                    }
                }
            }catch(Exception e) {
                Console.WriteLine(e.ToString());
            }
		}

		//private unsafe void SendBinary(byte* data, int dataLength)
		public void SendBinary(byte[] data, int dataLength)
		{
            try
            {
                Console.WriteLine("call SendBinary func: data.Length, dataLength = " + data.Length.ToString() + ", " + dataLength.ToString());
                if (data == null || dataLength == 0) throw new Exception("Invalid data size");
                int size = dataLength, offset = 0;
                do
                {
                    int dataWrite = socket.Send(data, offset, size, SocketFlags.None);
                    if (dataWrite == 0) break;
                    offset += dataWrite;
                    size -= dataWrite;
                }
                while (size != 0);
            }catch(Exception ex){
                Console.WriteLine(ex);
            }
        }

		//public unsafe void SendImage(Bitmap bitmap, int screenWidth, int screenHeight, int screenIndex, bool compress, int targetFPS)
		public void SendImage(BitmapXama bitmap, int screenWidth, int screenHeight, int screenIndex, bool compress, float targetFPS)		
		{
			try
			{
				// get data length
				int dataLength, imageDataSize;
                imageDataSize = bitmap.Width * bitmap.Height * 3; //PixelFormat.Format24bprRgb

				dataLength = imageDataSize;


                //            // compress if needed
                if (GlobalConfiguration.isStreamRawH264Data)
                {
                    dataLength = bitmap.getInternalBuffer().Length;
                }
                else if (GlobalConfiguration.isConvJpeg)
                {
                    if (compressedStream == null) compressedStream = new MemoryStream();
                    else compressedStream.SetLength(0);

                    var tmpBitmapArr = bitmap.getInternalBuffer();
                    Array.Resize<Byte>(ref tmpBitmapArr, imageDataSize);
                    var img = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Bgr565>(tmpBitmapArr, bitmap.Width, bitmap.Height);
                    var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder();
                    encoder.Quality = GlobalConfiguration.jpegEncodeQuality; //default value is 75
                    img.Save(compressedStream, encoder);
                    compressedStream.Flush();
                    dataLength = (int) compressedStream.Length;
                }else if(compress)
                {
                    if (compressedStream == null) compressedStream = new MemoryStream();
                    else compressedStream.SetLength(0);

                    //using (var bitmapStream = new UnmanagedMemoryStream((byte*)locked.Scan0, dataLength))
                    var tmpBitmapArr = bitmap.getInternalBuffer();
                    Array.Resize<Byte>(ref tmpBitmapArr, imageDataSize);
                    var bitmapStream = new MemoryStream(tmpBitmapArr);

                    using (var gzip = new GZipStream(compressedStream, CompressionMode.Compress, true))
                    {
                        bitmapStream.CopyTo(gzip);
                    }

                    compressedStream.Flush();
                    dataLength = (int)compressedStream.Length;
                }

                // send meta data
                var metaData = new MetaData()
				{
					type = MetaDataTypes.ImageData,
					compressed = compress,
					dataSize = dataLength,
                    //dataSize = -1, // for Debug
					imageDataSize = imageDataSize,
					width = (short)bitmap.Width,
					height = (short)bitmap.Height,
					screenWidth = (short)screenWidth,
					screenHeight = (short)screenHeight,
					//screenIndex = (short)screenIndex,
					//format = bitmap.PixelFormat,
					targetFPS = targetFPS
                    //mouseX = (short) frameNumber
				};
                frameNumber++;

				SendMetaDataInternal(metaData);

                if (compress || GlobalConfiguration.isConvJpeg)
                {
                    SendBinary(compressedStream.GetBuffer(), dataLength);
                }
                else
                {
                    // send bitmap data
                    SendBinary(bitmap.getInternalBuffer(), dataLength);
                }
            }
			catch (Exception ex)
			{
                throw ex;
			}
		}

        // if all element value is zero, returns false
        private bool checkSeriarizedMetaData(byte[] data, int dataLength)
        {
            for(int idx = 0; idx < dataLength; idx++)
            {
                if(data[idx] != 0)
                {
                    return true;
                }
            }
            return false;
        }

		private void SendMetaDataInternal(MetaData metaData)
		{
            //MemoryStream ms = new MemoryStream();
            //BinaryFormatter bf = new BinaryFormatter();
            //bf.Serialize(ms, metaData);
            //byte[] buf = ms.ToArray();
            //if(checkSeriarizedMetaData(buf, (int) ms.Length) == false)
            //{
            //    throw new Exception("seriarized MetaData is all zero!!!");
            //}
            //Console.WriteLine("check MetaData object serialized binary size");
            //Console.WriteLine(metaDataSize);
            //Console.WriteLine(ms.Length);
            //if(metaDataSize != ms.Length)
            //{
            //    throw new Exception("serialized MetaData binary size is not constant...");
            //}


            byte[] bytes = new byte[metaDataSize];

            GCHandle gchw = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            Marshal.StructureToPtr(metaData, gchw.AddrOfPinnedObject(), false);
            gchw.Free();
            SendBinary(bytes, metaDataSize);
		}

        public void SendMetaData(MetaData metaData)
        {
            try
            {
                SendMetaDataInternal(metaData);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private static bool IsConnected(Socket socket)
		{
			if (socket == null || !socket.Connected) return false;

            return true;

			//try
			//{
			//	return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
			//}
			//catch
			//{
			//	return false;
			//}
		}

		public bool IsConnected()
		{
			lock (this)
			{
				return !disconnected && IsConnected(socket);
			}
		}

		private void FireConnectionFailedCallback(string error)
		{
			if (ConnectionFailedCallback != null) ConnectionFailedCallback(error);
		}

		private void FireConnectedCallback()
		{
			if (ConnectedCallback != null) ConnectedCallback();
		}

		private void FireDisconnectedCallback()
		{
			if (DisconnectedCallback != null) DisconnectedCallback();
		}

		private void FireDataRecievedCallback(byte[] data, int dataSize, int offset)
		{
			if (DataRecievedCallback != null) DataRecievedCallback(data, dataSize, offset);
		}

		private void FireStartDataRecievedCallback(MetaData metaData)
		{
			if (StartDataRecievedCallback != null) StartDataRecievedCallback(metaData);
		}

		private void FireEndDataRecievedCallback()
		{
			if (EndDataRecievedCallback != null) EndDataRecievedCallback();
		}
	}
}
