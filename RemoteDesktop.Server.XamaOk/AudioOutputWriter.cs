﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RemoteDesktop.Server.XamaOK;
using RemoteDesktop.Android.Core;
using NAudio.Wave.Compression;
using System.Media;
using NAudio.Wave.SampleProviders;
using RemoteDesktop.Android.Core.Sound;
using System.Net;
using NAudio.MediaFoundation;
using System.Threading;

namespace RemoteDesktop.Server.XamaOK
{

    public sealed class AudioOutputWriter : IDisposable
    {

        #region イベント

        //public event EventHandler<WaveInEventArgs> DataAvailable;

        #endregion

        #region フィールド

        private WasapiLoopbackCapture _WaveIn;
        private SoundDataSocket sdsock;
        private MMDevice m_device;
        public bool IsRecording = false;
        private GlobalConfiguration config;

        private uint m_JitterBufferCount = 20; // max buffering num of RTPPacket at jitter buffer
        private uint m_Milliseconds = 20; // time period of jitter buffer (msec)
        private MemoryStream debug_ms = new MemoryStream();
        private MemoryStream captured_buf = new MemoryStream();
        private OpusEncoderManager m_opusEncoder;

        #endregion

        #region コンストラクタ

        // this call block until recieved client connection request
        public AudioOutputWriter(MMDevice device, GlobalConfiguration config)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            this.config = config;
            m_JitterBufferCount = this.config.JitterBuffer;
            m_Milliseconds = this.config.JitterBufferTimerPeriodMsec;

            m_device = device;
            this._WaveIn = new WasapiLoopbackCapture(m_device);
            this._WaveIn.DataAvailable += this.WaveInOnDataAvailable;
            this._WaveIn.RecordingStopped += this.WaveInOnRecordingStopped;
            WaveFormat wfmt = this._WaveIn.WaveFormat;

                sdsock = new SoundDataSocket(NetworkTypes.Server);
                //dispatcher = Dispatcher.CurrentDispatcher;
                sdsock.ConnectedCallback += Socket_ConnectedCallback;
                sdsock.DisconnectedCallback += Socket_DisconnectedCallback;
                sdsock.ConnectionFailedCallback += Socket_ConnectionFailedCallback;
                sdsock.DataRecievedCallback += Socket_DataRecievedCallback;
                sdsock.StartDataRecievedCallback += Socket_StartDataRecievedCallback;
                sdsock.EndDataRecievedCallback += Socket_EndDataRecievedCallback;
            //sdsock.Listen(IPAddress.Parse(GlobalConfiguration.ServerAddress), this.config.SoundServerPort);
            sdsock.Listen(IPAddress.Any, this.config.SoundServerPort);

            Start(); // start capture and send stream
        }

        #endregion

        #region プロパティ

        #endregion

        #region メソッド

        public void Start()
        {
            this.IsRecording = true;
            this._WaveIn.StartRecording();

        }

        private void resetAllInstanseState()
        {
            this._WaveIn.RecordingStopped -= this.WaveInOnRecordingStopped;
            this._WaveIn.StopRecording();
            this._WaveIn.Dispose();
            this._WaveIn = null;
            if(GlobalConfiguration.isEncodeWithOpus && m_opusEncoder != null)
            {
                m_opusEncoder.Dispose();
                m_opusEncoder = null;
            }

            //------

            this._WaveIn = new WasapiLoopbackCapture(m_device);
            this._WaveIn.DataAvailable += this.WaveInOnDataAvailable;
            this._WaveIn.RecordingStopped += this.WaveInOnRecordingStopped;

            sdsock.ReListen();

            Start(); // start capture and send stream
        }

        #region オーバーライド
        #endregion

        #region イベントハンドラ

        private void WaveInOnRecordingStopped(object sender, StoppedEventArgs e)
        {
        }

        private byte[] convert32bitFloat48000HzStereoPCMTo16bitMonoPCM(WaveInEventArgs e, int sampleRate)
        {
            byte[] recorded_buf = e.Buffer;
            int recorded_length = e.BytesRecorded;

            byte[] result_buf = null;
            int result_len = -1;

            try
            {
                //// 生データを再生可能なデータに変換
                var waveBufferResample = new BufferedWaveProvider(this._WaveIn.WaveFormat);
                waveBufferResample.DiscardOnBufferOverflow = true;
                waveBufferResample.ReadFully = false;  // leave a buffer?
                waveBufferResample.BufferLength = recorded_length;
                var sampleStream = new WaveToSampleProvider(waveBufferResample);

                // Downsample
                var resamplingProvider = new WdlResamplingSampleProvider(sampleStream, sampleRate);

                // Stereo to mono
                var monoProvider = new StereoToMonoSampleProvider(resamplingProvider)
                {
                    LeftVolume = 1f,
                    RightVolume = 1f
                };

                // Convert to 32bit float to 16bit PCM
                var ieeeToPcm = new SampleToWaveProvider16(monoProvider);

                waveBufferResample.AddSamples(recorded_buf, 0, recorded_length);

                result_len = recorded_length / (2 * (48000 / sampleRate) * 2); // depth conv and sampling and ch conv
                result_buf = new byte[result_len];
                ieeeToPcm.Read(result_buf, 0, result_len);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("exit...");
                System.Windows.Forms.Application.Exit();
            }

            return result_buf;
        }

        private byte[] convert32bitFloat48000HzStereoPCMTo16bitMonoPCM_Alpha(WaveInEventArgs e, int sampleRate)
        {
            byte[] recorded_buf = e.Buffer;
            int recorded_length = e.BytesRecorded;

            byte[] result_buf = null;
            int result_len = -1;

            try
            {
                //// 生データを再生可能なデータに変換
                var waveBufferResample = new BufferedWaveProvider(this._WaveIn.WaveFormat);
                waveBufferResample.DiscardOnBufferOverflow = true;
                waveBufferResample.ReadFully = false;  // leave a buffer?
                waveBufferResample.BufferLength = recorded_length;
                var sampleStream = new WaveToSampleProvider(waveBufferResample);

                // Downsample
                var resamplingProvider = new WdlResamplingSampleProvider(sampleStream, sampleRate);

                // Stereo to mono
                var monoProvider = new StereoToMonoSampleProvider(resamplingProvider)
                {
                    LeftVolume = 1f,
                    RightVolume = 1f
                };

                // Convert to 32bit float to 16bit PCM
                var ieeeToPcm = new SampleToWaveProvider16(monoProvider);
                var depthConvertProvider = new WaveFormatConversionProvider(new WaveFormat(sampleRate, 8, 1), ieeeToPcm);
                var depthConvertProviderRev = new WaveFormatConversionProvider(new WaveFormat(sampleRate, 16, 1), depthConvertProvider);

                waveBufferResample.AddSamples(recorded_buf, 0, recorded_length);

                result_len = recorded_length / (2 * (48000 / sampleRate) * 2); // depth conv and sampling and ch conv
                result_buf = new byte[result_len];
                depthConvertProviderRev.Read(result_buf, 0, result_len);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("exit...");
                System.Windows.Forms.Application.Exit();
            }

            return result_buf;
        }
/*
        private byte[] convertIEEE32bitFloatTo8bitPCMAndEncodeToMP3(WaveInEventArgs e)
        {
            byte[] recorded_buf = e.Buffer;
            int recorded_length = e.BytesRecorded;
            if(recorded_length == 0)
            {
                return null;
            }

            //byte[] depthConv_buf = null;
            //int depthConvBytes = -1;

            byte[] pcm16_buf = null;
            int pcm16_len = -1;

            //byte[] pcm8_buf = null;

            byte[] mp3_buf = null;
            try
            {
                //// 生データを再生可能なデータに変換
                var waveBufferResample = new BufferedWaveProvider(this._WaveIn.WaveFormat);
                waveBufferResample.DiscardOnBufferOverflow = true;
                waveBufferResample.ReadFully = false;  // leave a buffer?
                waveBufferResample.BufferLength = recorded_length;
                var sampleStream = new WaveToSampleProvider(waveBufferResample);

                // Downsample to 8000
                var resamplingProvider = new WdlResamplingSampleProvider(sampleStream, RTPConfiguration.SamplesPerSecond);

                // Stereo to mono
                var monoProvider = new StereoToMonoSampleProvider(resamplingProvider)
                {
                    LeftVolume = 1f,
                    RightVolume = 1f
                };

                // Convert to 32bit float to 16bit PCM
                var ieeeToPcm = new SampleToWaveProvider16(monoProvider);

                waveBufferResample.AddSamples(recorded_buf, 0, recorded_length);

                mp3_buf = SoundEncodeUtil.encodePCMtoMP3(ieeeToPcm);

                ////Convert 16bit PCM to 8bit PCM
                //var depthConvertProvider = new WaveFormatConversionProvider(new WaveFormat(rtp_config.SamplesPerSecond, 8, 1), ieeeToPcm);

                //pcm16_len = recorded_length / (2 * 6 * 2);
                //pcm16_buf = new byte[pcm16_len];
                //ieeeToPcm.Read(pcm16_buf, 0, pcm16_len);
                //var depthConvertStream = new WaveFormatConversionStream(new WaveFormat(rtp_config.SamplesPerSecond, 8, 1), new RawSourceWaveStream(pcm16_buf, 0, pcm16_len, new WaveFormat(rtp_config.SamplesPerSecond, 16, 1)));

                //// データを源流から流す
                //waveBufferResample.AddSamples(recorded_buf, 0, recorded_length);

                //int pcm8_len = pcm16_len / 2;
                //pcm8_buf = new byte[pcm8_len];
                //depthConvertStream.Read(pcm8_buf, 0, pcm8_len);

                //mp3_buf = SoundEncodeUtil.encodePCMtoMP3(depthConvertProvider);

                
                //var waveBufferResample = new BufferedWaveProvider(this._WaveIn.WaveFormat);
                //waveBufferResample.AddSamples(recorded_buf, 0, recorded_length);
                //mp3_buf = SoundEncodeUtil.encodePCMtoMP3(waveBufferResample);
                
                Console.WriteLine(Utils.getFormatedCurrentTime() + " converted 32bit float 64KHz stereo " + recorded_length.ToString()  + " bytes to 16bit PCM 8KHz mono and encode it to mp3 compressed data " + mp3_buf.Length.ToString() + " bytes");
            } catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("exit...");
                System.Windows.Forms.Application.Exit();
            }

            return mp3_buf;
        }
*/
        public void handleDataWithTCP(byte[] sdata_buf)
        {
            Console.WriteLine("call handleDataWithTcp");
            if (!sdsock.IsConnected())
            {
                return;
            }
            // こういうケースがあるようだ
            if(sdata_buf.Length == 0)
            {
                return;
            }
            Console.WriteLine("call SoundUtils.ToRTPPacket");
            RTPPacket rtp = SoundUtils.ToRTPPacket(sdata_buf, config);
            Console.WriteLine("call sdsock.SendRTPPacket");
            sdsock.SendRTPPacket(rtp, config.compress, GlobalConfiguration.SamplesPerSecond, config.BitsPerSample, config.Channels, config.isConvertMulaw);
        }

        private void WaveInOnDataAvailable(object sender, WaveInEventArgs e)
        {
            //Console.WriteLine($"{DateTime.Now:yyyy/MM/dd hh:mm:ss.fff} : {e.BytesRecorded} bytes");

            if (GlobalConfiguration.isRunCapturedSoundDataHndlingWithoutConn == false && sdsock.IsConnected() == false)
            {
                return;
            }

            if (e.BytesRecorded == 0)
            {
                return;
            }


            if (GlobalConfiguration.isUseFFMPEG)
            {
                if (GlobalConfiguration.caputuedPcmBufferSamples == 0)
                {
                    if (e.BytesRecorded > 0)
                    {
                        MainApplicationContext.ffmpegProc.StandardInput.BaseStream.Write(e.Buffer, 0, e.BytesRecorded);
                        MainApplicationContext.ffmpegProc.StandardInput.BaseStream.Flush();
                    }
                }
                else
                {
                    captured_buf.Write(e.Buffer, 0, e.BytesRecorded);
                    int needed_samples = GlobalConfiguration.caputuedPcmBufferSamples; //1024 * 100; //1024;
                                                                                    // 指定されたサンプル数が溜まったら書き込む (adtsでは 1フレーム = 1024サンプル)
                    if (captured_buf.Length / (4 * 2) >= needed_samples)
                    {
                        Console.WriteLine(Utils.getFormatedCurrentTime() + " DEBUG: pass " + needed_samples.ToString() + " samples to ffmpeg");
                        captured_buf.Position = 0;
                        byte[] tmp_buf = new byte[4 * 2 * needed_samples];
                        captured_buf.Read(tmp_buf, 0, tmp_buf.Length);
                        MainApplicationContext.ffmpegProc.StandardInput.BaseStream.Write(tmp_buf, 0, tmp_buf.Length);
                        MemoryStream new_ms = new MemoryStream();

                        // 残ったデータの処理
                        captured_buf.Position = 4 * 2 * needed_samples;
                        byte[] left_data_buf = new byte[captured_buf.Length - 4 * 2 * needed_samples];
                        captured_buf.Read(left_data_buf, 0, left_data_buf.Length);
                        captured_buf.Position = 0;
                        captured_buf.SetLength(0);
                        captured_buf.Write(left_data_buf, 0, left_data_buf.Length);

                        MainApplicationContext.ffmpegProc.StandardInput.BaseStream.Flush();
                    }
                }
            }
            else if (GlobalConfiguration.isEncodeWithOpus)
            {
                if (m_opusEncoder == null)
                {
//                    m_opusEncoder = new OpusEncoderManager(this, GlobalConfiguration.SamplesPerSecond);
                    m_opusEncoder = new OpusEncoderManager(this, GlobalConfiguration.SampleRateDummyForSoundEnDecoder);
                }

                byte[] conved_pcm = convert32bitFloat48000HzStereoPCMTo16bitMonoPCM(e, GlobalConfiguration.SamplesPerSecond);
                //byte[] conved_pcm = convert32bitFloat48000HzStereoPCMTo16bitMonoPCM_Alpha(e, GlobalConfiguration.SamplesPerSecond);
                Console.WriteLine(Utils.getFormatedCurrentTime() + " DEBUG: pass " + conved_pcm.Length.ToString() + " bytes to opus encoder");

                // エンコーダクラスが流量制御をして送信まで行う
                m_opusEncoder.addPCMSamples(conved_pcm, conved_pcm.Length);
            }
            else
            {
                throw new Exception("condition is invalid");
            }
        }

		private void Socket_StartDataRecievedCallback(PacketHeader pktHdr)
		{
            // do nothing
		}

		private void Socket_EndDataRecievedCallback()
		{
			// do nothing
		}

		private void Socket_DataRecievedCallback(byte[] data, int dataSize, int offset)
		{
			// do nothing
		}

        // only used on Client
		private void Socket_ConnectionFailedCallback(string error)
		{
			Console.WriteLine("Failed to connect: " + error);
		}

		private void Socket_ConnectedCallback()
		{
            Console.WriteLine("Connected to client");
		}

		private void Socket_DisconnectedCallback()
		{
			Console.WriteLine("Disconnected from client");
			MainApplicationContext.dispatcher.InvokeAsync(delegate()
			{
				//sdsock.ReListen(); // resetAllInstanseStateでやるので不要
                resetAllInstanseState();
			});
		}

        #endregion

        #region ヘルパーメソッド
        #endregion

        #endregion

        #region IDisposable メンバー

        public void Dispose()
        {


            if(this._WaveIn != null)
            {
                this._WaveIn.StopRecording();
                this._WaveIn.DataAvailable -= this.WaveInOnDataAvailable;
                this._WaveIn.RecordingStopped -= this.WaveInOnRecordingStopped;
                this._WaveIn.Dispose();
            }

        }

        #endregion

    }

}
