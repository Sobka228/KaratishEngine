using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace KaratishEngine
{
    public enum ObjectType { Cube, Sphere, Triangle, PlayerSpawn, Trigger, Circle }

    public class MapObject
    {
        public Vector3 Position;
        public Vector3 Size = Vector3.One;
        public ObjectType Type;
        public bool IsSelected = false;
        public Vector3 Color = new Vector3(1f, 0f, 0f);
        public float RotationY = 0;
        public bool HasPhysics = false;
        public Vector3 Velocity = Vector3.Zero;
        public string TriggerType = "None";
        public event Action<MapObject, MapObject>? OnEnterTrigger;
        public int TextureId = -1;

        public void InvokeTrigger(MapObject other) => OnEnterTrigger?.Invoke(this, other);
    }

    public class Mesh
    {
        public int Vao { get; private set; }
        public int Vbo { get; private set; }
        public int Ebo { get; private set; }
        public int IndexCount { get; private set; }

        public Mesh(float[] vertices, uint[] indices)
        {
            IndexCount = indices.Length;
            Vao = GL.GenVertexArray();
            Vbo = GL.GenBuffer();
            Ebo = GL.GenBuffer();

            GL.BindVertexArray(Vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, Vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, Ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            int stride = (3 + 3 + 2) * sizeof(float);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));

            GL.BindVertexArray(0);
        }
    }

    public static class CollisionHandler
    {
        public static bool IsCollidingAABB(MapObject a, MapObject b)
        {
            Vector3 minA = a.Position - a.Size / 2f;
            Vector3 maxA = a.Position + a.Size / 2f;
            Vector3 minB = b.Position - b.Size / 2f;
            Vector3 maxB = b.Position + b.Size / 2f;

            bool x = maxA.X >= minB.X && minA.X <= maxB.X;
            bool y = maxA.Y >= minB.Y && minA.Y <= maxB.Y;
            bool z = maxA.Z >= minB.Z && minA.Z <= maxB.Z;
            return x && y && z;
        }
    }

    public class Shader
    {
        public int Handle { get; private set; }

        public Shader(string vertSrc, string fragSrc)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertSrc);
            GL.CompileShader(vs);
            CheckShaderCompile(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragSrc);
            GL.CompileShader(fs);
            CheckShaderCompile(fs);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vs);
            GL.AttachShader(Handle, fs);
            GL.LinkProgram(Handle);
            CheckProgramLink(Handle);

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private void CheckShaderCompile(int shader)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                throw new Exception("Shader compile error: " + log);
            }
        }
        private void CheckProgramLink(int prog)
        {
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetProgramInfoLog(prog);
                throw new Exception("Program link error: " + log);
            }
        }

        public void Use() => GL.UseProgram(Handle);

        public int GetUniformLocation(string name) => GL.GetUniformLocation(Handle, name);

        public void SetMatrix4(string name, ref Matrix4 m)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) GL.UniformMatrix4(loc, false, ref m);
        }

        public void SetVector3(string name, Vector3 v)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) GL.Uniform3(loc, v);
        }

        public void SetInt(string name, int value)
        {
            int loc = GetUniformLocation(name);
            if (loc >= 0) GL.Uniform1(loc, value);
        }
    }

    public class ShadowMap
    {
        public int Fbo { get; private set; }
        public int DepthTexture { get; private set; }
        public int Size { get; private set; }

        public ShadowMap(int size = 2048)
        {
            Size = size;
            Fbo = GL.GenFramebuffer();
            DepthTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Size, Size, 0, OpenTK.Graphics.OpenGL4.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            float[] border = new float[] { 1f, 1f, 1f, 1f };
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, DepthTexture, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new Exception("Shadow framebuffer incomplete: " + status);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }
    }

    public class Engine
    {
        private List<MapObject> entities = new List<MapObject>();
        public IReadOnlyList<MapObject> Entities => entities.AsReadOnly();

        private Dictionary<ObjectType, Mesh> meshes = new Dictionary<ObjectType, Mesh>();
        private Shader? mainShader;
        private Shader? depthShader;
        private ShadowMap? shadowMap;

        public Vector3 CameraPos = new Vector3(3, 3, 3);
        public Vector3 LightPos = new Vector3(5, 10, 5);

        private const float Gravity = -9.81f;
        private const float FloorY = 0f;

        public void Initialize()
        {
            GL.ClearColor(0.07f, 0.07f, 0.08f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            CreateShaders();
            shadowMap = new ShadowMap(2048);

            CreateMeshes();
        }

        public void AddObject(MapObject o) => entities.Add(o);
        public void RemoveObject(MapObject o) => entities.Remove(o);

        public void Update(float dt)
        {
            foreach (var obj in entities)
            {
                if (obj.HasPhysics)
                {
                    obj.Velocity.Y += Gravity * dt;
                    obj.Position += obj.Velocity * dt;
                    float half = obj.Size.Y / 2f;
                    if (obj.Position.Y < FloorY + half)
                    {
                        obj.Position.Y = FloorY + half;
                        obj.Velocity.Y = 0;
                    }
                }
            }
            CheckTriggers();
        }

        private void CheckTriggers()
        {
            var triggers = entities.Where(e => e.Type == ObjectType.Trigger).ToList();
            foreach (var d in entities)
            {
                if (d.Type == ObjectType.Trigger) continue;
                foreach (var t in triggers)
                {
                    if (CollisionHandler.IsCollidingAABB(d, t))
                    {
                        t.InvokeTrigger(d);
                        if (t.TriggerType.Equals("Death", StringComparison.OrdinalIgnoreCase)) d.Color = new Vector3(0.8f, 0, 0);
                    }
                }
            }
        }

        public void Render(int width, int height)
        {
            if (mainShader == null || depthShader == null || shadowMap == null) return;

            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), width / (float)Math.Max(1, height), 0.1f, 100f);
            Matrix4 view = Matrix4.LookAt(CameraPos, Vector3.Zero, Vector3.UnitY);

            Matrix4 lightView = Matrix4.LookAt(LightPos, Vector3.Zero, Vector3.UnitY);
            float near = 1f, far = 50f;
            Matrix4 lightProj = Matrix4.CreateOrthographicOffCenter(-20, 20, -20, 20, near, far); // directional-style
            Matrix4 lightSpace = lightProj * lightView;

            GL.Viewport(0, 0, shadowMap.Size, shadowMap.Size);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowMap.Fbo);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            GL.CullFace(CullFaceMode.Front); // reduce peter-panning
            depthShader.Use();
            depthShader.SetMatrix4("lightSpaceMatrix", ref lightSpace);

            foreach (var obj in entities)
            {
                if (!meshes.TryGetValue(obj.Type, out var mesh)) continue;
                Matrix4 model = Matrix4.CreateScale(obj.Size) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RotationY)) * Matrix4.CreateTranslation(obj.Position);
                depthShader.SetMatrix4("model", ref model);
                GL.BindVertexArray(mesh.Vao);
                GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.CullFace(CullFaceMode.Back);

            GL.Viewport(0, 0, width, height);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            mainShader.Use();
            mainShader.SetMatrix4("projection", ref projection);
            mainShader.SetMatrix4("view", ref view);
            mainShader.SetVector3("viewPos", CameraPos);
            mainShader.SetVector3("lightPos", LightPos);
            mainShader.SetMatrix4("lightSpaceMatrix", ref lightSpace);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, shadowMap.DepthTexture);
            mainShader.SetInt("shadowMap", 1);

            foreach (var obj in entities)
            {
                if (!meshes.TryGetValue(obj.Type, out var mesh)) continue;
                Matrix4 model = Matrix4.CreateScale(obj.Size) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RotationY)) * Matrix4.CreateTranslation(obj.Position);
                mainShader.SetMatrix4("model", ref model);
                mainShader.SetVector3("objectColor", obj.Color);

                GL.BindVertexArray(mesh.Vao);
                GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        private void CreateShaders()
        {
            string depthVert = @"#version 330 core
layout (location = 0) in vec3 aPos;
uniform mat4 model;
uniform mat4 lightSpaceMatrix;
void main() {
    gl_Position = lightSpaceMatrix * model * vec4(aPos, 1.0);
}";
            string depthFrag = @"#version 330 core
void main() { }
";
            depthShader = new Shader(depthVert, depthFrag);

            string vert = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTex;

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;
out vec4 FragPosLightSpace;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 lightSpaceMatrix;

void main()
{
    FragPos = vec3(model * vec4(aPos,1.0));
    Normal = mat3(transpose(inverse(model))) * aNormal;
    FragPosLightSpace = lightSpaceMatrix * vec4(FragPos, 1.0);
    TexCoord = aTex;
    gl_Position = projection * view * vec4(FragPos, 1.0);
}
";
            string frag = @"#version 330 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec4 FragPosLightSpace;
in vec2 TexCoord;

uniform vec3 objectColor;
uniform vec3 lightPos;
uniform vec3 viewPos;
uniform sampler2D shadowMap;

float ShadowCalculation(vec4 fragPosLightSpace)
{
    // perform perspective divide
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    // get closest depth from shadow map (using [0,1] range)
    float closestDepth = texture(shadowMap, projCoords.xy).r;
    float currentDepth = projCoords.z;
    // simple bias
    float bias = max(0.005 * (1.0 - dot(normalize(Normal), normalize(lightPos - FragPos))), 0.0005);
    // PCF
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    for(int x = -1; x <= 1; ++x)
    {
        for(int y = -1; y <= 1; ++y)
        {
            float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x,y) * texelSize).r;
            shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;
        }
    }
    shadow /= 9.0;
    if(projCoords.z > 1.0) shadow = 0.0;
    return shadow;
}

void main()
{
    vec3 color = objectColor;
    // ambient
    vec3 ambient = 0.15 * color;
    // diffuse
    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(lightPos - FragPos);
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * color;
    // specular
    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 specular = vec3(0.3) * spec;

    float shadow = ShadowCalculation(FragPosLightSpace);
    vec3 lighting = (ambient + (1.0 - shadow) * (diffuse + specular));
    FragColor = vec4(lighting, 1.0);
}
";
            mainShader = new Shader(vert, frag);
            mainShader.Use();
            mainShader.SetInt("shadowMap", 1);
        }

        private void CreateMeshes()
        {
            float[] cubeVerts = {
                -0.5f, -0.5f,  0.5f,  0,0,1,  0,0,
                 0.5f, -0.5f,  0.5f,  0,0,1,  1,0,
                 0.5f,  0.5f,  0.5f,  0,0,1,  1,1,
                -0.5f,  0.5f,  0.5f,  0,0,1,  0,1,

                -0.5f, -0.5f, -0.5f,  0,0,-1, 0,0,
                 0.5f, -0.5f, -0.5f,  0,0,-1, 1,0,
                 0.5f,  0.5f, -0.5f,  0,0,-1, 1,1,
                -0.5f,  0.5f, -0.5f,  0,0,-1, 0,1,

                -0.5f, -0.5f, -0.5f, -1,0,0, 0,0,
                -0.5f, -0.5f,  0.5f, -1,0,0, 1,0,
                -0.5f,  0.5f,  0.5f, -1,0,0, 1,1,
                -0.5f,  0.5f, -0.5f, -1,0,0, 0,1,

                 0.5f, -0.5f, -0.5f, 1,0,0, 0,0,
                 0.5f, -0.5f,  0.5f, 1,0,0, 1,0,
                 0.5f,  0.5f,  0.5f, 1,0,0, 1,1,
                 0.5f,  0.5f, -0.5f, 1,0,0, 0,1,

                -0.5f, 0.5f, -0.5f, 0,1,0, 0,0,
                -0.5f, 0.5f,  0.5f, 0,1,0, 1,0,
                 0.5f, 0.5f,  0.5f, 0,1,0, 1,1,
                 0.5f, 0.5f, -0.5f, 0,1,0, 0,1,

                -0.5f, -0.5f, -0.5f, 0,-1,0, 0,0,
                -0.5f, -0.5f,  0.5f, 0,-1,0, 1,0,
                 0.5f, -0.5f,  0.5f, 0,-1,0, 1,1,
                 0.5f, -0.5f, -0.5f, 0,-1,0, 0,1,
            };
            uint[] cubeIdx = {
                0,1,2, 2,3,0,
                4,5,6, 6,7,4,
                8,9,10, 10,11,8,
                12,13,14, 14,15,12,
                16,17,18, 18,19,16,
                20,21,22, 22,23,20
            };
            meshes.Add(ObjectType.Cube, new Mesh(cubeVerts, cubeIdx));

            float[] triVerts = {
                -0.5f, -0.5f, 0f,  0,0,1, 0,0,
                 0.5f, -0.5f, 0f,  0,0,1, 1,0,
                 0f,  0.5f, 0f,   0,0,1, 0.5f,1,
            };
            uint[] triIdx = { 0, 1, 2 };
            meshes.Add(ObjectType.Triangle, new Mesh(triVerts, triIdx));

            var verts = new List<float>();
            var idx = new List<uint>();
            int sectors = 32, stacks = 16; float radius = 0.5f;
            for (int i = 0; i <= stacks; ++i)
            {
                float stackAngle = MathF.PI / 2 - i * MathF.PI / stacks;
                float xy = radius * MathF.Cos(stackAngle);
                float z = radius * MathF.Sin(stackAngle);
                for (int j = 0; j <= sectors; ++j)
                {
                    float sectorAngle = j * 2 * MathF.PI / sectors;
                    float x = xy * MathF.Cos(sectorAngle);
                    float y = xy * MathF.Sin(sectorAngle);

                    verts.Add(x); verts.Add(y); verts.Add(z);

                    Vector3 n = new Vector3(x, y, z).Normalized();
                    verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);

                    verts.Add(j / (float)sectors); verts.Add(i / (float)stacks);
                }
            }
            for (int i = 0; i < stacks; ++i)
            {
                uint k1 = (uint)(i * (sectors + 1));
                uint k2 = (uint)((i + 1) * (sectors + 1));
                for (int j = 0; j < sectors; ++j, ++k1, ++k2)
                {
                    if (i != 0) { idx.Add(k1); idx.Add(k2); idx.Add(k1 + 1); }
                    if (i != (stacks - 1)) { idx.Add(k1 + 1); idx.Add(k2); idx.Add(k2 + 1); }
                }
            }
            meshes.Add(ObjectType.Sphere, new Mesh(verts.ToArray(), idx.ToArray()));
        }

        public int LoadTexture(string path)
        {
            using var image = Image.Load<Rgba32>(path);
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }
    }
}
