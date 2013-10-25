﻿using System;

using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Sample
{
    class GroundMaterial : Material
    {
        /// <summary>
        /// Size in bytes of the material buffer.
        /// </summary>
        private static int BufferSize = 16;

        public Double Albedo
        {
            get { return (Double)Bar[Prefix + "albedo"].Value; }
            set { Bar[Prefix + "albedo"].Value = value; }
        }

        public String ColorMap { get; set; }

        private Buffer constantBuffer;

        private PixelShader pixelShader;

        public GroundMaterial(Device device, TweakBar bar, String name)
            : base(device, bar, name)
        {
            bar.AddFloat(Prefix + "albedo", "Albedo", name, 0, 100, 30, 0.1, 2);

            pixelShader = Material.CompileShader(device, "ground");

            constantBuffer = Material.AllocateMaterialBuffer(device, BufferSize);
        }

        public override void BindMaterial(DeviceContext context, ResourceProxy proxy)
        {
            using (DataStream stream = new DataStream(BufferSize, true, true))
            {
                stream.Write<float>((float)Albedo);
                Material.CopyStream(context, constantBuffer, stream);
            }

            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(2, constantBuffer);
            context.PixelShader.SetShaderResource(1, proxy[ColorMap]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pixelShader.Dispose();
                constantBuffer.Dispose();

                Bar.RemoveVariable(Prefix + "albedo");
            }
        }
    }
}
