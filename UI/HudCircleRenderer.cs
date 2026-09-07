// Ported from CommonLib (MIT, DArkHekRoMaNT), itself based on
// https://github.com/copygirl/CarryCapacity/blob/master/src/Client/HudOverlayRenderer.cs

using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace PlayerCorpse.UI
{
    /// <summary>
    /// Draws a progress ring at the crosshair. One instance serves the whole client; see <see cref="CorpseInteractHud"/>.
    /// </summary>
    public class HudCircleRenderer : IRenderer
    {
        private readonly HudCircleSettings _settings;
        private readonly ICoreClientAPI _capi;

        private MeshRef? _circleMesh;
        private float _meshProgress = -1f;
        private float _circleAlpha;
        private float _circleProgress;

        public bool CircleVisible { get; set; }

        /// <summary>Ring fill fraction, 0..1. Setting it also makes the ring visible.</summary>
        public float CircleProgress
        {
            get => _circleProgress;
            set
            {
                _circleProgress = GameMath.Clamp(value, 0f, 1f);
                CircleVisible = true;
            }
        }

        public double RenderOrder => 0;
        public int RenderRange => 10;

        public HudCircleRenderer(ICoreClientAPI capi, HudCircleSettings settings)
        {
            _capi = capi;
            _settings = settings;
            _capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "playercorpse-interact-ring");
            UpdateCircleMesh(1f);
        }

        private void UpdateCircleMesh(float progress)
        {
            if (progress == _meshProgress)
            {
                return;
            }
            _meshProgress = progress;

            float ringSize = _settings.InnerRadius / _settings.OuterRadius;
            float stepSize = 1f / _settings.MaxSteps;

            int steps = 1 + (int)Math.Ceiling(_settings.MaxSteps * progress);
            var data = new MeshData(steps * 2, steps * 6, false, false, true, false);

            for (int i = 0; i < steps; i++)
            {
                double p = Math.Min(progress, i * stepSize) * Math.PI * 2;
                float x = (float)Math.Sin(p);
                float y = -(float)Math.Cos(p);

                data.AddVertexSkipTex(x, y, 0);
                data.AddVertexSkipTex(x * ringSize, y * ringSize, 0);

                if (i > 0)
                {
                    data.AddIndices([i * 2 - 2, i * 2 - 1, i * 2]);
                    data.AddIndices([i * 2, i * 2 - 1, i * 2 + 1]);
                }
            }

            if (_circleMesh is not null)
            {
                _capi.Render.UpdateMesh(_circleMesh, data);
            }
            else
            {
                _circleMesh = _capi.Render.UploadMesh(data);
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            _circleAlpha = GameMath.Clamp(
                _circleAlpha + deltaTime / (CircleVisible ? _settings.AlphaIn : -_settings.AlphaOut), 0f, 1f);

            if (CircleProgress <= 0f || _circleAlpha <= 0f || _circleMesh is null)
            {
                return;
            }

            UpdateCircleMesh(CircleProgress);

            IRenderAPI rend = _capi.Render;
            IShaderProgram shader = rend.CurrentActiveShader;

            float r = ((_settings.Color >> 16) & 0xFF) / 255f;
            float g = ((_settings.Color >> 8) & 0xFF) / 255f;
            float b = (_settings.Color & 0xFF) / 255f;

            shader.Uniform("rgbaIn", new Vec4f(r, g, b, _circleAlpha));
            shader.Uniform("extraGlow", 0);
            shader.Uniform("applyColor", 0);
            shader.Uniform("tex2d", 0);
            shader.Uniform("noTexture", 1f);
            shader.UniformMatrix("projectionMatrix", rend.CurrentProjectionMatrix);

            int x, y;
            if (_capi.Input.MouseGrabbed)
            {
                x = rend.FrameWidth / 2;
                y = rend.FrameHeight / 2;
            }
            else
            {
                x = _capi.Input.MouseX;
                y = _capi.Input.MouseY;
            }

            rend.GlPushMatrix();
            rend.GlTranslate(x, y, 0);
            rend.GlScale(_settings.OuterRadius, _settings.OuterRadius, 0);
            shader.UniformMatrix("modelViewMatrix", rend.CurrentModelviewMatrix);
            rend.GlPopMatrix();
            rend.RenderMesh(_circleMesh);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            if (_circleMesh is not null)
            {
                _capi.Render.DeleteMesh(_circleMesh);
                _circleMesh = null;
            }
        }
    }

    public class HudCircleSettings
    {
        /// <summary>Ring color as 0xRRGGBB.</summary>
        public int Color { get; set; } = 0xCCCCCC;

        /// <summary>Fade-in time in seconds.</summary>
        public float AlphaIn { get; set; } = 0.2f;

        /// <summary>Fade-out time in seconds.</summary>
        public float AlphaOut { get; set; } = 0.4f;

        public int MaxSteps { get; set; } = 16;
        public float OuterRadius { get; set; } = 24;
        public float InnerRadius { get; set; } = 18;
    }
}
