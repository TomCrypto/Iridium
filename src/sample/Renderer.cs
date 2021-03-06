﻿using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;

using SharpDX.DXGI;
using SharpDX.Windows;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;
using DriverType = SharpDX.Direct3D.DriverType;

using Insight;

namespace Sample
{
    /// <summary>
    /// The sample's render pipeline.
    /// </summary>
    class Renderer : IDisposable
    {
        private DeviceContext context;
        private SwapChain swapChain;
        private Factory factory;
        private Device device;

        private GraphicsResource hdrBuffer;
        private GraphicsResource ldrBuffer;
        private GraphicsResource resolved;

        private DiffractionOptions options = new DiffractionOptions();
        private OpticalProfile profile = new OpticalProfile();
        private EyeDiffraction eyeDiffraction;
        private ToneMapper toneMapper;
        private TweakBar mainBar;
        private Scene scene;

        private Stopwatch timer = new Stopwatch();
        private double currentTime;

        #region DirectX Resources Initialization

        private void InitializeGraphicsDevice(RenderForm window)
        {
#if DEBUG
            var flags = DeviceCreationFlags.Debug;
#else
            var flags = DeviceCreationFlags.None;
#endif

            Device.CreateWithSwapChain(DriverType.Hardware, flags, new SwapChainDescription()
            {
                BufferCount = 1,
                IsWindowed = true,
                Flags = SwapChainFlags.None,
                OutputHandle = window.Handle,
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                ModeDescription = new ModeDescription()
                {
                    Width = 0, Height = 0,
                    Format = Format.R8G8B8A8_UNorm,
                    RefreshRate = new Rational(60, 1),
                }
            }, out device, out swapChain);

            context = device.ImmediateContext;
            factory = swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAll);
        }

        private void InitializeResources(Size dimensions)
        {
            if (  toneMapper != null) toneMapper.Dispose();
            if (   hdrBuffer != null) hdrBuffer.Dispose();
            if (   ldrBuffer != null) ldrBuffer.Dispose();
            if (    resolved != null) resolved.Dispose();

            swapChain.ResizeBuffers(0, 0, 0, Format.Unknown, SwapChainFlags.None);

            toneMapper   = new ToneMapper(device, dimensions, (Double)mainBar["exposure"].Value, (Double)mainBar["gamma"].Value);
            resolved     = new GraphicsResource(device, dimensions, Format.R32G32B32A32_Float, true, true);
            ldrBuffer    = new GraphicsResource(device, swapChain.GetBackBuffer<Texture2D>(0));

            /* hdrBuffer is a bit special since it can be multisampled - create this one manually. */
            hdrBuffer = new GraphicsResource(device, new Texture2D(device, new Texture2DDescription()
            {
                ArraySize = 1,
                MipLevels = 1,
                Width = dimensions.Width,
                Height = dimensions.Height,
                Usage = ResourceUsage.Default,
                Format = Format.R32G32B32A32_Float,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                SampleDescription = Settings.MultisamplingOptions,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            }));
        }

        #endregion

        #region General Parameters Configuration

        #region Bar Listeners

        private void QualityChange(object sender, Variable variable)
        {
            eyeDiffraction.Quality = (RenderQuality)((int)variable.Value);
        }

        private void RotationChange(object sender, Variable variable)
        {
            scene.RotationSensitivity = (Double)variable.Value;
        }

        private void MovementChange(object sender, Variable variable)
        {
            Settings.movementSensitivity = (float)((Double)variable.Value);
        }

        private void ExposureChange(object sender, Variable variable)
        {
            toneMapper.Exposure = (Double)variable.Value;
        }

        private void GammaChange(object sender, Variable variable)
        {
            toneMapper.Gamma = (Double)variable.Value;
        }

        private void FNumberChange(object sender, Variable variable)
        {
            profile.FNumber = (Double)variable.Value;
        }

        private void GlareChange(object sender, Variable variable)
        {
            profile.Glare = (Double)variable.Value;
        }

        private void SizeChange(object sender, Variable variable)
        {
            profile.Size = (Double)variable.Value;
        }

        private void ScaleCorrectChange(object sender, Variable variable)
        {
            options.ScaleCorrection = (Boolean)variable.Value;
        }

        private void FieldOfViewChange(object sender, Variable variable)
        {
            scene.FieldOfView = (float)(Double)variable.Value;
        }

        #endregion

        private void InitializeTweakBar()
        {
            if (!TweakBar.InitializeLibrary(device)) throw new ExternalException("Failed to initialize AntTweakBar!");
            else
            {
                mainBar = new TweakBar(null, "Configuration Options");

                /* Configuration options go below. */

                mainBar.AddFloat("gamma", "Gamma", "General", 1, 3, 2.2, 0.05, 3, "Gamma response to calibrate to the monitor.");
                mainBar.AddFloat("exposure", "Exposure", "General", 0.001, 1.5, 0.001, 0.001, 3, "Exposure level at which to render the scene.");
                mainBar.AddBoolean("diffraction", "Enable", "Diffraction", "Yes", "No", true, "Whether to display diffraction effects or not.");
                mainBar.AddInteger("quality", "Quality", "Diffraction", 1, 4, (int)Settings.quality, 1, "The quality of the diffraction effects (from 1 to 4).");
                mainBar.AddFloat("fnumber", "f-number", "Diffraction", 1, 16, 1.5, 0.05, 2, "The f-number at which to simulate the aperture.");
                mainBar.AddFloat("glare", "Glare", "Diffraction", 0, 1, 0, 0.01, 2);
                mainBar.AddFloat("size", "Size", "Diffraction", 0, 1, 0.5, 0.01, 2);

                mainBar.AddBoolean("scale_correct", "Scale Correction", "Diffraction", "Yes", "No", true);

                mainBar.AddFloat("rotation_sensitivity", "Rotation", "Navigation", 0, 5, Settings.rotationSensitivity, 0.01, 2, "The sensitivity of mouse rotation.");
                mainBar.AddFloat("movement_sensitivity", "Movement", "Navigation", 0, 1, Settings.movementSensitivity, 0.01, 2, "The sensitivity of keyboard movement.");

                mainBar.AddFloat("field_of_view", "Field Of View", "Navigation", 10, 120, 75, 1, 2, "The sensitivity of keyboard movement.");

                /* TweakBar listeners go below. */

                mainBar["quality"].VariableChange += QualityChange;

                mainBar["exposure"].VariableChange += ExposureChange;
                mainBar["gamma"].VariableChange += GammaChange;

                mainBar["fnumber"].VariableChange += FNumberChange;
                mainBar["glare"].VariableChange += GlareChange;
                mainBar["size"].VariableChange += SizeChange;
                mainBar["scale_correct"].VariableChange += ScaleCorrectChange;

                mainBar["rotation_sensitivity"].VariableChange += RotationChange;
                mainBar["movement_sensitivity"].VariableChange += MovementChange;
                mainBar["field_of_view"].VariableChange += FieldOfViewChange;
            }
        }

        #endregion

        #region Render Subsystems Initialization

        private void InitializeInsight()
        {
            // initialize profile here

            profile.FNumber = (Double)mainBar["fnumber"].Value;
            options.ScaleCorrection = (Boolean)mainBar["scale_correct"].Value;
            profile.Size = (Double)mainBar["size"].Value;

            if (eyeDiffraction != null) eyeDiffraction.Dispose();
            eyeDiffraction = new EyeDiffraction(device, context, Settings.quality, profile, options);
        }

        private void InitializeScene(RenderForm window)
        {
            scene = new Scene(device, context, window, window.ClientSize);
            scene.RotationSensitivity = (float)(Double)mainBar["rotation_sensitivity"].Value;
        }

        #endregion

        /// <summary>
        /// Creates a new Renderer instance.
        /// </summary>
        /// <param name="window">The window to render into.</param>
        public Renderer(RenderForm window)
        {
            window.ResizeEnd += ResizeWindow;
            InitializeGraphicsDevice(window);
            InitializeTweakBar();

            InitializeResources(window.ClientSize);
            InitializeScene(window);
            InitializeInsight();
            timer.Start();
        }

        /// <summary>
        /// Reads user input and updates the state
        /// of the renderer. No rendering in here.
        /// </summary>
        public void Update()
        {
            scene.Update();
        }

        /// <summary>
        /// Renders the scene to the backbuffer.
        /// </summary>
        public void Render()
        {
            /* First we render our sample scene into the hdrBuffer. */
            scene.Render(hdrBuffer.RTV, context, eyeDiffraction.Pass);

            /* We were potentially rendering into a multisampled backbuffer, now resolve it to a normal one. */
            context.ResolveSubresource(hdrBuffer.Resource, 0, resolved.Resource, 0, Format.R32G32B32A32_Float);

            /* If we're doing diffraction, call the right function. Note we are doing rendering in-place into the resolved texture. */
            if ((Boolean)mainBar["diffraction"].Value) eyeDiffraction.Render(resolved.Dimensions, resolved.RTV, resolved.SRV, Tick());

            /* Finally, tone-map the frame in the low-dynamic-range backbuffer texture. */
            toneMapper.ToneMap(context, eyeDiffraction.Pass, ldrBuffer.RTV, resolved.SRV);

            /* Render bars. */
            TweakBar.Render();

            /* Send the backbuffer to the screen. */
            swapChain.Present(1, PresentFlags.None);
        }

        #region Miscellaneous

        /// <summary>
        /// Returns the time elapsed since the last call, in seconds.
        /// </summary>
        private double Tick()
        {
            double elapsed = (double)timer.ElapsedTicks / (double)Stopwatch.Frequency;
            double delta = elapsed - currentTime; currentTime = elapsed; return delta;
        }

        /// <summary>
        /// Called when the window is resized. This should
        /// resize the swapchain and update all resources.
        /// </summary>
        private void ResizeWindow(object sender, EventArgs e)
        {
            RenderForm window = (RenderForm)sender;
            
            /* Update if the window has actually changed. */
            if (scene.RenderDimensions != window.ClientSize)
            {
                scene.RenderDimensions = window.ClientSize;
                InitializeResources(window.ClientSize);
                TweakBar.UpdateWindow(window);
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Destroys this Renderer instance.
        /// </summary>
        ~Renderer()
        {
            Dispose(false);
        }

        /// <summary>
        /// Disposes of all used resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                eyeDiffraction.Dispose();
                toneMapper.Dispose();
                ldrBuffer.Dispose();
                hdrBuffer.Dispose();
                resolved.Dispose();
                mainBar.Dispose();
                scene.Dispose();
                timer.Stop();

                TweakBar.FinalizeLibrary();
                context.ClearState();
                context.Flush();

                swapChain.Dispose();
                context.Dispose();
                factory.Dispose();
                device.Dispose();
            }
        }

        #endregion
    }
}
