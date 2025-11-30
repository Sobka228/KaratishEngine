using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

namespace KaratishEngine
{
    public enum ObjectType { Cube, Sphere, PlayerSpawn, Trigger, Circle }
    public enum EditMode { None, Move, Rotate, Scale }

    public class MapObject
    {
        public Vector3 Position;
        public Vector3 Size = Vector3.One;
        public ObjectType Type;
        public bool IsSelected = false;
        public Vector3 Color = new Vector3(1f, 0f, 0f); // яркий красный по умолчанию
        public float RotationY = 0;
        public bool HasPhysics = false;
        public Vector3 Velocity = Vector3.Zero;
        public string TriggerType = "None";
        public int TextureId = -1;
    }

    public class Engine
    {
        private List<MapObject> mapObjects = new List<MapObject>();
        public IReadOnlyList<MapObject> MapObjects => mapObjects.AsReadOnly();

        private Vector3 cameraPos = new Vector3(3, 3, 3);
        private int cubeVao, cubeVbo, cubeEbo, shaderProgram;
        private int skyboxVao, skyboxVbo, skyboxShader, skyboxTexture;

        public void Initialize()
        {
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1f);
            GL.Enable(EnableCap.DepthTest);

            InitCubeData();
            InitShader();
            InitSkybox();
        }

        public void AddObject(MapObject obj) => mapObjects.Add(obj);
        public void RemoveObject(MapObject obj) => mapObjects.Remove(obj);

        public void Update(float deltaTime)
        {
            foreach (var obj in mapObjects)
            {
                if (obj.HasPhysics)
                {
                    obj.Velocity += new Vector3(0, -9.81f, 0) * deltaTime;
                    obj.Position += obj.Velocity * deltaTime;
                    if (obj.Position.Y < obj.Size.Y / 2.0f)
                    {
                        obj.Position.Y = obj.Size.Y / 2.0f;
                        obj.Velocity.Y = 0;
                    }
                }
            }
        }

        public void Render(int width, int height)
        {
            GL.Viewport(0, 0, width, height);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Matrix4 view = Matrix4.LookAt(cameraPos, Vector3.Zero, Vector3.UnitY);
            Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f),
                Math.Max(1, width) / (float)Math.Max(1, height), 0.1f, 100f);

            foreach (var obj in mapObjects)
            {
                DrawCube(obj.Position, obj.Size, obj.Color, obj.RotationY, view, proj, obj.TextureId);
            }

            GL.DepthFunc(DepthFunction.Lequal);

            Matrix4 viewNoTrans = new Matrix4(
                new Vector4(view.Row0.X, view.Row0.Y, view.Row0.Z, 0),
                new Vector4(view.Row1.X, view.Row1.Y, view.Row1.Z, 0),
                new Vector4(view.Row2.X, view.Row2.Y, view.Row2.Z, 0),
                new Vector4(0, 0, 0, 1)
            );

            GL.UseProgram(skyboxShader);
            GL.UniformMatrix4(GL.GetUniformLocation(skyboxShader, "view"), false, ref viewNoTrans);
            GL.UniformMatrix4(GL.GetUniformLocation(skyboxShader, "projection"), false, ref proj);

            GL.BindVertexArray(skyboxVao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.TextureCubeMap, skyboxTexture);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            GL.BindVertexArray(0);

            GL.DepthFunc(DepthFunction.Less);
            GL.Flush();
        }

        private void InitCubeData()
        {
            float[] vertices = {
                -0.5f,-0.5f,-0.5f, 0.5f,-0.5f,-0.5f, 0.5f,0.5f,-0.5f, -0.5f,0.5f,-0.5f,
                -0.5f,-0.5f,0.5f, 0.5f,-0.5f,0.5f, 0.5f,0.5f,0.5f, -0.5f,0.5f,0.5f
            };
            uint[] indices = {
                0,1,2,2,3,0, 4,5,6,6,7,4,
                0,1,5,5,4,0, 2,3,7,7,6,2,
                0,3,7,7,4,0, 1,2,6,6,5,1
            };

            cubeVao = GL.GenVertexArray();
            cubeVbo = GL.GenBuffer();
            cubeEbo = GL.GenBuffer();

            GL.BindVertexArray(cubeVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, cubeVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, cubeEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        private void InitShader()
        {
            string vertex = @"#version 330 core
                layout(location=0) in vec3 aPos;
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 proj;
                void main() { gl_Position = proj * view * model * vec4(aPos,1.0); }";
            string fragment = @"#version 330 core
                out vec4 FragColor;
                uniform vec3 color;
                void main() { FragColor = vec4(color,1.0); }";

            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertex);
            GL.CompileShader(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragment);
            GL.CompileShader(fs);

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vs);
            GL.AttachShader(shaderProgram, fs);
            GL.LinkProgram(shaderProgram);

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private void DrawCube(Vector3 pos, Vector3 scale, Vector3 color, float rotationY, Matrix4 view, Matrix4 proj, int textureId = -1)
        {
            GL.UseProgram(shaderProgram);
            Matrix4 model = Matrix4.CreateScale(scale) *
                            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotationY)) *
                            Matrix4.CreateTranslation(pos);

            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "model"), false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "proj"), false, ref proj);
            GL.Uniform3(GL.GetUniformLocation(shaderProgram, "color"), color);

            GL.BindVertexArray(cubeVao);
            GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void InitSkybox()
        {
            float[] skyboxVertices = new float[36 * 3]; // пустой куб
            skyboxVao = GL.GenVertexArray();
            skyboxVbo = GL.GenBuffer();
            GL.BindVertexArray(skyboxVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, skyboxVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, skyboxVertices.Length * sizeof(float), skyboxVertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        public int LoadTexture(string path)
        {
            using var image = Image.Load<Rgba32>(path);
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                image.Width, image.Height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                PixelType.UnsignedByte, pixels
            );

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            return tex;
        }
    }
}
