﻿using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace OpenH264.Encoder
{
    public delegate void H264RawDataHandler(byte[] encodedData);

    //このプロジェクトのコードから必要な機能だけを抽出したクラス
    public class ExtractedH264Encoder
    {
        private OpenH264Lib.Encoder encoder;
        public event H264RawDataHandler encodedDataGenerated;
        private int timestamp = 0; // equal frame number
        private int width;
        private int height;
        private int bps;
        private float fps;
        private float keyFrameInterval;

        public ExtractedH264Encoder(int width, int height, int bps, float fps, float keyFrameInterval)
        {
            this.width = width;
            this.height = height;
            this.bps = bps;
            this.fps = fps;
            this.keyFrameInterval = keyFrameInterval;

            setup(width, height, bps, fps, keyFrameInterval);
        }
        
        public void setup(int width, int height, int bps, float fps, float keyFrameInterval)
        {
            var exe_path = System.AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Thread.Sleep(30000);
            // H264エンコーダーを作成
            encoder = new OpenH264Lib.Encoder(exe_path + "\\openh264-1.7.0-win32.dll");

            // 1フレームエンコードするごとにライターに書き込み
            OpenH264Lib.Encoder.OnEncodeCallback onEncode = (data, length, frameType) => { };

            OpenH264Lib.Encoder.OnEncodeCallback onEncodeProxy = (data, length, frameType) =>
            {
                onEncode(data, length, frameType);
            };


            onEncode = (data, length, frameType) =>
            {
                lock (this)
                {
                    var keyFrame = (frameType == OpenH264Lib.Encoder.FrameType.IDR) || (frameType == OpenH264Lib.Encoder.FrameType.I);
                    Console.WriteLine("Encord {0} bytes, data.Length: {1} bytes, KeyFrame:{2} timestamp:{3} " + frameType.ToString(), length, data.Length, keyFrame, timestamp);

                    encodedDataGenerated(data);
                    timestamp++;
                }
            };

            // H264エンコーダーの設定
            //encoder.Setup(width, height, 5000000, fps, 10.0f, onEncode);
            encoder.Setup(width, height, bps, fps, keyFrameInterval, onEncode);
        }

        public void addBitmapFrame(byte[] data, int frameNumber)
        {
            lock (this)
            {
                byte[] copy_buf = new byte[data.Length];
                Array.Copy(data, 0, copy_buf, 0, data.Length);
                var bmp = new Bitmap(new MemoryStream(copy_buf));
                encoder.Encode(bmp, frameNumber);
                bmp.Dispose();
            }
        }

        public void reInit()
        {
            try
            {
                encoder.Dispose();
                encoder = null;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
            setup(width, height, bps, fps, keyFrameInterval);
        }
    }
}
