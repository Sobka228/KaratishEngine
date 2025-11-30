using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KaratishEngine
{
    public class MeshRenderer
    {
        public int Vao { get; private set; }
        public int Vbo { get; private set; }
        public int Ebo { get; private set; }
        public int IndexCount { get; private set; }

        public MeshRenderer(float[] vertices, uint[] indices)
        {
            IndexCount = indices.Length;

            Vao = GL.GenVertexArray();
            Vbo = GL.GenBuffer();
            Ebo = GL.GenBuffer();

            GL.BindVertexArray(Vao);

            // Буфер вершин
            GL.BindBuffer(BufferTarget.ArrayBuffer, Vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, Ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Указываем атрибут вершин (позиция)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            GL.BindVertexArray(0);
        }
    }

    public enum ObjectType { Cube, Sphere, Triangle, PlayerSpawn, Trigger, Circle }
    public enum EditMode { None, Move, Rotate, Scale }
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

        public void InvokeTrigger(MapObject other)
        {
            // Используем безопасный вызов: OnEnterTrigger?
            OnEnterTrigger?.Invoke(other, this);
        }
    }

    public static class CollisionHandler
    {
        public static bool IsCollidingAABB(MapObject obj1, MapObject obj2)
        {

            Vector3 minA = obj1.Position - obj1.Size / 2.0f;
            Vector3 maxA = obj1.Position + obj1.Size / 2.0f;

            Vector3 minB = obj2.Position - obj2.Size / 2.0f;
            Vector3 maxB = obj2.Position + obj2.Size / 2.0f;

            bool collisionX = maxA.X >= minB.X && minA.X <= maxB.X;
            bool collisionY = maxA.Y >= minB.Y && minA.Y <= maxB.Y;
            bool collisionZ = maxA.Z >= minB.Z && minA.Z <= maxB.Z;

            return collisionX && collisionY && collisionZ;
        }
    }

    public class Engine
    {
        private List<MapObject> entities = new List<MapObject>();
        public IReadOnlyList<MapObject> Entities => entities.AsReadOnly();

        private Vector3 cameraPos = new Vector3(3, 3, 3);
        private int shaderProgram;
        private int skyboxVao, skyboxVbo;
        private int skyboxShader = 0;

        private int skyboxTexture = 0;

        private Dictionary<ObjectType, MeshRenderer> meshes = new Dictionary<ObjectType, MeshRenderer>();

        private const float Gravity = -9.81f;
        private const float FloorY = 0.0f;

        public void Initialize()
        {
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            InitShader();
            InitSkybox();
            InitCubeData();
            InitSphereData(32, 32);
            InitTriangleData();
        }

        public void AddObject(MapObject obj) => entities.Add(obj);
        public void RemoveObject(MapObject obj) => entities.Remove(obj);

        public void Update(float deltaTime)
        {
            foreach (var obj in entities)
            {
                if (obj.HasPhysics)
                {
                    obj.Velocity.Y += Gravity * deltaTime;
                    obj.Position += obj.Velocity * deltaTime;
                    float halfHeight = obj.Size.Y / 2.0f;
                    if (obj.Position.Y < FloorY + halfHeight)
                    {
                        obj.Position.Y = FloorY + halfHeight;
                        obj.Velocity.Y = 0;
                    }
                }
            }
            CheckTriggers();
        }
        private void CheckTriggers()
        {
            var triggerObjects = entities.Where(e => e.Type == ObjectType.Trigger).ToList();
            foreach (var dynamicObj in entities)
            {
                if (dynamicObj.Type == ObjectType.Trigger) continue;

                foreach (var triggerObj in triggerObjects)
                {
                    if (CollisionHandler.IsCollidingAABB(dynamicObj, triggerObj))
                    {
                        Console.WriteLine($"[TRIGGER]: Объект типа {dynamicObj.Type} вошел в триггер '{triggerObj.TriggerType}'.");
                        triggerObj.InvokeTrigger(dynamicObj);
                        if (triggerObj.TriggerType.Equals("Death", StringComparison.OrdinalIgnoreCase))
                        {
                            dynamicObj.Color = new Vector3(0.8f, 0.0f, 0.0f);
                        }
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

            foreach (var obj in entities)
            {
                if (meshes.TryGetValue(obj.Type, out var mesh))
                {
                    DrawMesh(mesh, obj.Position, obj.Size, obj.Color, obj.RotationY, view, proj, obj.TextureId);
                }
            }
            if (skyboxTexture != 0)
            {
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
            }

            GL.Flush();
        }

        private void InitCubeData()
        {
            float[] vertices = {
                // positions (только позиции)
                -0.5f,-0.5f,-0.5f, 0.5f,-0.5f,-0.5f, 0.5f,0.5f,-0.5f, -0.5f,0.5f,-0.5f,
                -0.5f,-0.5f,0.5f, 0.5f,-0.5f,0.5f, 0.5f,0.5f,0.5f, -0.5f,0.5f,0.5f
            };
            uint[] indices = {
                0,1,2, 2,3,0, // Back
                4,5,6, 6,7,4, // Front
                0,1,5, 5,4,0, // Bottom
                2,3,7, 7,6,2, // Top
                0,3,7, 7,4,0, // Left
                1,2,6, 6,5,1  // Right
            };
            meshes.Add(ObjectType.Cube, new MeshRenderer(vertices, indices));
        }

        private void InitTriangleData()
        {
            float[] vertices = {
                -0.5f, -0.5f, 0.0f,
                 0.5f, -0.5f, 0.0f,
                 0.0f,  0.5f, 0.0f
            };
            uint[] indices = {
                0, 1, 2
            };
            meshes.Add(ObjectType.Triangle, new MeshRenderer(vertices, indices));
        }

        private void InitSphereData(int sectorCount, int stackCount)
        {
            var vertices = new List<float>();
            var indices = new List<uint>();
            float radius = 0.5f;
            float x, y, z, xy;
            float sectorStep = 2 * MathF.PI / sectorCount;
            float stackStep = MathF.PI / stackCount;
            float sectorAngle, stackAngle;

            for (int i = 0; i <= stackCount; ++i)
            {
                stackAngle = MathF.PI / 2 - i * stackStep;
                xy = radius * MathF.Cos(stackAngle);
                z = radius * MathF.Sin(stackAngle);

                for (int j = 0; j <= sectorCount; ++j)
                {
                    sectorAngle = j * sectorStep;

                    x = xy * MathF.Cos(sectorAngle);
                    y = xy * MathF.Sin(sectorAngle);

                    vertices.Add(x);
                    vertices.Add(y);
                    vertices.Add(z);
                }
            }

            uint k1, k2;
            for (int i = 0; i < stackCount; ++i)
            {
                k1 = (uint)(i * (sectorCount + 1));
                k2 = (uint)((i + 1) * (sectorCount + 1));

                for (int j = 0; j < sectorCount; ++j, ++k1, ++k2)
                {
                    if (i != 0)
                    {
                        indices.Add(k1);
                        indices.Add(k2);
                        indices.Add(k1 + 1);
                    }

                    if (i != (stackCount - 1))
                    {
                        indices.Add(k1 + 1);
                        indices.Add(k2);
                        indices.Add(k2 + 1);
                    }
                }
            }
            meshes.Add(ObjectType.Sphere, new MeshRenderer(vertices.ToArray(), indices.ToArray()));
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

            shaderProgram = CreateShader(vertex, fragment);
        }

        private int CreateShader(string vertexSource, string fragmentSource)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertexSource);
            GL.CompileShader(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragmentSource);
            GL.CompileShader(fs);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return program;
        }

        private void DrawMesh(MeshRenderer mesh, Vector3 pos, Vector3 scale, Vector3 color, float rotationY, Matrix4 view, Matrix4 proj, int textureId = -1)
        {
            GL.UseProgram(shaderProgram);

            Matrix4 model = Matrix4.CreateScale(scale) *
                            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotationY)) *
                            Matrix4.CreateTranslation(pos);

            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "model"), false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "proj"), false, ref proj);
            GL.Uniform3(GL.GetUniformLocation(shaderProgram, "color"), color);

            GL.BindVertexArray(mesh.Vao);
            GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void InitSkybox()
        {
            string skyboxVertex = @"#version 330 core
                layout (location = 0) in vec3 aPos;
                out vec3 TexCoords;
                uniform mat4 projection;
                uniform mat4 view;
                void main()
                {
                    TexCoords = aPos;
                    vec4 pos = projection * view * vec4(aPos, 1.0);
                    gl_Position = pos.xyww; 
                }";
            string skyboxFragment = @"#version 330 core
                out vec4 FragColor;
                in vec3 TexCoords;
                uniform samplerCube skybox;
                void main()
                {  
                    FragColor = texture(skybox, TexCoords);
                }";

            skyboxShader = CreateShader(skyboxVertex, skyboxFragment);
            GL.UseProgram(skyboxShader);
            GL.Uniform1(GL.GetUniformLocation(skyboxShader, "skybox"), 0);
            float[] skyboxVertices = {
                -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f, 1.0f, -1.0f, -1.0f,
                 1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f, -1.0f,

                -1.0f, -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f, -1.0f,
                -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f,

                 1.0f, -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
                 1.0f,  1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,

                -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
                 1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,

                -1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,
                 1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f,

                -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,
                 1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f
            };
            skyboxVao = GL.GenVertexArray();
            skyboxVbo = GL.GenBuffer();
            GL.BindVertexArray(skyboxVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, skyboxVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, skyboxVertices.Length * sizeof(float), skyboxVertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        public void SetSkybox(int textureId)
        {
            skyboxTexture = textureId;
        }

        public int LoadTexture(string path)
        {
            // Метод LoadTexture для 2D текстур
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

        public int LoadCubemap(List<string> paths)
        {
            if (paths.Count != 6)
            {
                throw new ArgumentException("Для скайбокса требуется ровно 6 путей к текстурам.", nameof(paths));
            }

            int texID = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, texID);

            for (int i = 0; i < paths.Count; i++)
            {
                using var image = Image.Load<Rgba32>(paths[i]);

                var pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i,
                    0, PixelInternalFormat.Rgba,
                    image.Width, image.Height, 0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                    PixelType.UnsignedByte, pixels
                );
            }

            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.TextureCubeMap, 0);

            return texID;
        }
    }
}