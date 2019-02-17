﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ImGuiNET
{
	/// <summary>
	/// ImGui renderer for use with XNA-likes (FNA & MonoGame)
	/// </summary>
	public class ImGuiRenderer
	{
		public ImFontPtr defaultFontPtr { get; private set; }

		// Graphics
		BasicEffect _effect;
		RasterizerState _rasterizerState;

		readonly VertexDeclaration _vertexDeclaration;
		readonly int _vertexDeclarationSize;

		byte[] _vertexData;
		VertexBuffer _vertexBuffer;
		int _vertexBufferSize;

		byte[] _indexData;
		IndexBuffer _indexBuffer;
		int _indexBufferSize;

		// Textures
		Dictionary<IntPtr, Texture2D> _loadedTextures = new Dictionary<IntPtr, Texture2D>();

		int _textureId;
		IntPtr? _fontTextureId;

		// Input
		int _scrollWheelValue;

		List<int> _keys = new List<int>();

		public ImGuiRenderer( Game game )
		{
			unsafe { _vertexDeclarationSize = sizeof( ImDrawVert ); }
			_vertexDeclaration = new VertexDeclaration(
				_vertexDeclarationSize,
				// Position
				new VertexElement( 0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0 ),
				// UV
				new VertexElement( 8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0 ),
				// Color
				new VertexElement( 16, VertexElementFormat.Color, VertexElementUsage.Color, 0 )
			);

			ImGui.SetCurrentContext( ImGui.CreateContext() );

			_rasterizerState = new RasterizerState()
			{
				CullMode = CullMode.None,
				DepthBias = 0,
				FillMode = FillMode.Solid,
				MultiSampleAntiAlias = false,
				ScissorTestEnable = true,
				SlopeScaleDepthBias = 0
			};

			setupInput();
		}

		#region ImGuiRenderer

		/// <summary>
		/// Creates a texture and loads the font data from ImGui. Should be called when the <see cref="GraphicsDevice" /> is initialized but before any rendering is done
		/// </summary>
		public unsafe void rebuildFontAtlas()
		{
			// Get font texture from ImGui
			var io = ImGui.GetIO();

			defaultFontPtr = ImGui.GetIO().Fonts.AddFontDefault();

			io.Fonts.GetTexDataAsRGBA32( out byte* pixelData, out int width, out int height, out int bytesPerPixel );

			// Copy the data to a managed array
			var pixels = new byte[width * height * bytesPerPixel];
			Marshal.Copy( new IntPtr( pixelData ), pixels, 0, pixels.Length );

			// Create and register the texture as an XNA texture
			var tex2d = new Texture2D( Core.graphicsDevice, width, height, false, SurfaceFormat.Color );
			tex2d.SetData( pixels );

			// Should a texture already have been built previously, unbind it first so it can be deallocated
			if( _fontTextureId.HasValue )
				unbindTexture( _fontTextureId.Value );

			// Bind the new texture to an ImGui-friendly id
			_fontTextureId = bindTexture( tex2d );

			// Let ImGui know where to find the texture
			io.Fonts.SetTexID( _fontTextureId.Value );
			io.Fonts.ClearTexData(); // Clears CPU side texture data
		}

		/// <summary>
		/// Creates a pointer to a texture, which can be passed through ImGui calls such as <see cref="ImGui.Image" />. That pointer is then used by ImGui to let us know what texture to draw
		/// </summary>
		public IntPtr bindTexture( Texture2D texture )
		{
			var id = new IntPtr( _textureId++ );
			_loadedTextures.Add( id, texture );
			return id;
		}

		/// <summary>
		/// Removes a previously created texture pointer, releasing its reference and allowing it to be deallocated
		/// </summary>
		public void unbindTexture( IntPtr textureId )
		{
			_loadedTextures.Remove( textureId );
		}

		/// <summary>
		/// Sets up ImGui for a new frame, should be called at frame start
		/// </summary>
		public void beforeLayout( float deltaTime )
		{
			ImGui.GetIO().DeltaTime = deltaTime;
			updateInput();
			ImGui.NewFrame();
		}

		/// <summary>
		/// Asks ImGui for the generated geometry data and sends it to the graphics pipeline, should be called after the UI is drawn using ImGui.** calls
		/// </summary>
		public void afterLayout()
		{
			ImGui.Render();
			unsafe { renderDrawData( ImGui.GetDrawData() ); }
		}

		#endregion ImGuiRenderer

		#region Setup & Update

		/// <summary>
		/// Maps ImGui keys to XNA keys. We use this later on to tell ImGui what keys were pressed
		/// </summary>
		void setupInput()
		{
			var io = ImGui.GetIO();

			_keys.Add( io.KeyMap[(int)ImGuiKey.Tab] = (int)Keys.Tab );
			_keys.Add( io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Keys.Left );
			_keys.Add( io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Keys.Right );
			_keys.Add( io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Keys.Up );
			_keys.Add( io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Keys.Down );
			_keys.Add( io.KeyMap[(int)ImGuiKey.PageUp] = (int)Keys.PageUp );
			_keys.Add( io.KeyMap[(int)ImGuiKey.PageDown] = (int)Keys.PageDown );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Home] = (int)Keys.Home );
			_keys.Add( io.KeyMap[(int)ImGuiKey.End] = (int)Keys.End );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Delete] = (int)Keys.Delete );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Backspace] = (int)Keys.Back );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Enter] = (int)Keys.Enter );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Escape] = (int)Keys.Escape );
			_keys.Add( io.KeyMap[(int)ImGuiKey.A] = (int)Keys.A );
			_keys.Add( io.KeyMap[(int)ImGuiKey.C] = (int)Keys.C );
			_keys.Add( io.KeyMap[(int)ImGuiKey.V] = (int)Keys.V );
			_keys.Add( io.KeyMap[(int)ImGuiKey.X] = (int)Keys.X );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Y] = (int)Keys.Y );
			_keys.Add( io.KeyMap[(int)ImGuiKey.Z] = (int)Keys.Z );


			// MonoGame-specific //////////////////////
			// _game.Window.TextInput += (s, a) =>
			// {
			//     if (a.Character == '\t') return;

			//     io.AddInputCharacter(a.Character);
			// };
			///////////////////////////////////////////

			// FNA-specific ///////////////////////////
			TextInputEXT.TextInput += c =>
			{
				if( c == '\t' ) return;

				ImGui.GetIO().AddInputCharacter( c );
			};
			///////////////////////////////////////////
		}

		/// <summary>
		/// Updates the <see cref="Effect" /> to the current matrices and texture
		/// </summary>
		Effect updateEffect( Texture2D texture )
		{
			_effect = _effect ?? new BasicEffect( Core.graphicsDevice );

			var io = ImGui.GetIO();

			// MonoGame-specific //////////////////////
			//var offset = .5f;
			///////////////////////////////////////////

			// FNA-specific ///////////////////////////
			var offset = 0f;
			///////////////////////////////////////////

			_effect.World = Matrix.Identity;
			_effect.View = Matrix.Identity;
			_effect.Projection = Matrix.CreateOrthographicOffCenter( offset, io.DisplaySize.X + offset, io.DisplaySize.Y + offset, offset, -1f, 1f );
			_effect.TextureEnabled = true;
			_effect.Texture = texture;
			_effect.VertexColorEnabled = true;

			return _effect;
		}

		/// <summary>
		/// Sends XNA input state to ImGui
		/// </summary>
		void updateInput()
		{
			var io = ImGui.GetIO();

			var mouse = Mouse.GetState();
			var keyboard = Keyboard.GetState();

			for( int i = 0; i < _keys.Count; i++ )
			{
				io.KeysDown[_keys[i]] = keyboard.IsKeyDown( (Keys)_keys[i] );
			}

			io.KeyShift = keyboard.IsKeyDown( Keys.LeftShift ) || keyboard.IsKeyDown( Keys.RightShift );
			io.KeyCtrl = keyboard.IsKeyDown( Keys.LeftControl ) || keyboard.IsKeyDown( Keys.RightControl );
			io.KeyAlt = keyboard.IsKeyDown( Keys.LeftAlt ) || keyboard.IsKeyDown( Keys.RightAlt );
			io.KeySuper = keyboard.IsKeyDown( Keys.LeftWindows ) || keyboard.IsKeyDown( Keys.RightWindows );

			io.DisplaySize = new System.Numerics.Vector2( Core.graphicsDevice.PresentationParameters.BackBufferWidth, Core.graphicsDevice.PresentationParameters.BackBufferHeight );
			io.DisplayFramebufferScale = new System.Numerics.Vector2( 1f, 1f );

			io.MousePos = new System.Numerics.Vector2( mouse.X, mouse.Y );

			io.MouseDown[0] = mouse.LeftButton == ButtonState.Pressed;
			io.MouseDown[1] = mouse.RightButton == ButtonState.Pressed;
			io.MouseDown[2] = mouse.MiddleButton == ButtonState.Pressed;

			var scrollDelta = mouse.ScrollWheelValue - _scrollWheelValue;
			io.MouseWheel = scrollDelta > 0 ? 1 : scrollDelta < 0 ? -1 : 0;
			_scrollWheelValue = mouse.ScrollWheelValue;
		}

		#endregion Setup & Update

		#region Internals

		/// <summary>
		/// Gets the geometry as set up by ImGui and sends it to the graphics device
		/// </summary>
		void renderDrawData( ImDrawDataPtr drawData )
		{
			// Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, vertex/texcoord/color pointers
			var lastViewport = Core.graphicsDevice.Viewport;
			var lastScissorBox = Core.graphicsDevice.ScissorRectangle;

			Core.graphicsDevice.BlendFactor = Color.White;
			Core.graphicsDevice.BlendState = BlendState.NonPremultiplied;
			Core.graphicsDevice.RasterizerState = _rasterizerState;
			Core.graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

			// Handle cases of screen coordinates != from framebuffer coordinates (e.g. retina displays)
			drawData.ScaleClipRects( ImGui.GetIO().DisplayFramebufferScale );

			// Setup projection
			Core.graphicsDevice.Viewport = new Viewport( 0, 0, Core.graphicsDevice.PresentationParameters.BackBufferWidth, Core.graphicsDevice.PresentationParameters.BackBufferHeight );

			updateBuffers( drawData );
			renderCommandLists( drawData );

			// Restore modified state
			Core.graphicsDevice.Viewport = lastViewport;
			Core.graphicsDevice.ScissorRectangle = lastScissorBox;
		}

		unsafe void updateBuffers( ImDrawDataPtr drawData )
		{
			if( drawData.TotalVtxCount == 0 )
			{
				return;
			}

			// Expand buffers if we need more room
			if( drawData.TotalVtxCount > _vertexBufferSize )
			{
				_vertexBuffer?.Dispose();

				_vertexBufferSize = (int)( drawData.TotalVtxCount * 1.5f );
				_vertexBuffer = new VertexBuffer( Core.graphicsDevice, _vertexDeclaration, _vertexBufferSize, BufferUsage.None );
				_vertexData = new byte[_vertexBufferSize * _vertexDeclarationSize];
			}

			if( drawData.TotalIdxCount > _indexBufferSize )
			{
				_indexBuffer?.Dispose();

				_indexBufferSize = (int)( drawData.TotalIdxCount * 1.5f );
				_indexBuffer = new IndexBuffer( Core.graphicsDevice, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None );
				_indexData = new byte[_indexBufferSize * sizeof( ushort )];
			}

			// Copy ImGui's vertices and indices to a set of managed byte arrays
			int vtxOffset = 0;
			int idxOffset = 0;

			for( var n = 0; n < drawData.CmdListsCount; n++ )
			{
				var cmdList = drawData.CmdListsRange[n];

				fixed ( void* vtxDstPtr = &_vertexData[vtxOffset * _vertexDeclarationSize] )
				fixed ( void* idxDstPtr = &_indexData[idxOffset * sizeof( ushort )] )
				{
					Buffer.MemoryCopy( (void*)cmdList.VtxBuffer.Data, vtxDstPtr, _vertexData.Length, cmdList.VtxBuffer.Size * _vertexDeclarationSize );
					Buffer.MemoryCopy( (void*)cmdList.IdxBuffer.Data, idxDstPtr, _indexData.Length, cmdList.IdxBuffer.Size * sizeof( ushort ) );
				}

				vtxOffset += cmdList.VtxBuffer.Size;
				idxOffset += cmdList.IdxBuffer.Size;
			}

			// Copy the managed byte arrays to the gpu vertex- and index buffers
			_vertexBuffer.SetData( _vertexData, 0, drawData.TotalVtxCount * _vertexDeclarationSize );
			_indexBuffer.SetData( _indexData, 0, drawData.TotalIdxCount * sizeof( ushort ) );
		}

		unsafe void renderCommandLists( ImDrawDataPtr drawData )
		{
			Core.graphicsDevice.SetVertexBuffer( _vertexBuffer );
			Core.graphicsDevice.Indices = _indexBuffer;

			int vtxOffset = 0;
			int idxOffset = 0;

			for( int n = 0; n < drawData.CmdListsCount; n++ )
			{
				var cmdList = drawData.CmdListsRange[n];
				for( int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++ )
				{
					var drawCmd = cmdList.CmdBuffer[cmdi];
					if( !_loadedTextures.ContainsKey( drawCmd.TextureId ) )
					{
						throw new InvalidOperationException( $"Could not find a texture with id '{drawCmd.TextureId}', please check your bindings" );
					}

					Core.graphicsDevice.ScissorRectangle = new Rectangle(
						(int)drawCmd.ClipRect.X,
						(int)drawCmd.ClipRect.Y,
						(int)( drawCmd.ClipRect.Z - drawCmd.ClipRect.X ),
						(int)( drawCmd.ClipRect.W - drawCmd.ClipRect.Y )
					);

					var effect = updateEffect( _loadedTextures[drawCmd.TextureId] );
					foreach( var pass in effect.CurrentTechnique.Passes )
					{
						pass.Apply();

#pragma warning disable CS0618 // // FNA does not expose an alternative method.
						Core.graphicsDevice.DrawIndexedPrimitives(
							primitiveType: PrimitiveType.TriangleList,
							baseVertex: vtxOffset,
							minVertexIndex: 0,
							numVertices: cmdList.VtxBuffer.Size,
							startIndex: idxOffset,
							primitiveCount: (int)drawCmd.ElemCount / 3
						);
#pragma warning restore CS0618
					}

					idxOffset += (int)drawCmd.ElemCount;
				}

				vtxOffset += cmdList.VtxBuffer.Size;
			}
		}

		#endregion Internals
	}
}