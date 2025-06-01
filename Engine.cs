﻿using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;
    private int _heartTextureId;
    private int _gameOverTextureId;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;
    private bool _isGameOver = false;
    private bool _isPaused = false;
    private bool _isNight = false;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;
        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var heartPath = Path.Combine("Assets", "heart.png");
        if (!File.Exists(heartPath))
            throw new FileNotFoundException("Heart image not found!", heartPath);

        _heartTextureId = _renderer.LoadTexture(heartPath, out var heartTexture);
        if (_heartTextureId == 0)
            throw new Exception("Failed to load heart texture.");

        var gameOverPath = Path.Combine("Assets", "game_over.png");
        if (!File.Exists(gameOverPath))
            throw new FileNotFoundException("Game Over image not found!", gameOverPath);

        _gameOverTextureId = _renderer.LoadTexture(gameOverPath, out _);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent) ?? throw new Exception("Failed to load level");

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent) ?? throw new Exception("Failed to load tile set");

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null) throw new Exception("Invalid level dimensions");
        if (level.TileWidth == null || level.TileHeight == null) throw new Exception("Invalid tile dimensions");

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;
        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;

        if (_isGameOver) return;

        bool togglePause = _input.IsKeyPPressed();
        if (togglePause)
        {
            _isPaused = !_isPaused;
            Console.WriteLine(_isPaused ? "Game Paused" : "Game Resumed");
            Thread.Sleep(200);
        }

        if (_isPaused) return;

        _lastUpdate = currentTime;

        if (_player == null) return;

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        if (isAttacking) _player.Attack();

        if (_player.Lives <= 0)
        {
            if (!_isGameOver)
            {
                Console.WriteLine("Game Over!");
                _isGameOver = true;
            }
            return;
        }

        _scriptEngine.ExecuteAll(this);

        if (addBomb)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }
        bool toggleNight = _input.IsKeyNPressed();
        if (toggleNight)
        {
            _isNight = !_isNight;
            Console.WriteLine(_isNight ? "Night mode ON" : "Day mode ON");
            Thread.Sleep(200); // debounce
        }

    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);
        RenderTerrain();
        RenderAllObjects();

        if (_isNight)
        {
            var (width, height) = _renderer.GetWindowSize();
            _renderer.SetDrawColor(15, 15, 40, 100); // overlay noapte
            _renderer.FillRect(new Rectangle<int>(0, 0, width, height));
        }

        _renderer.CameraLookAt(0, 0);
        for (int i = 0; i < _player.Lives; i++)
        {
            var heartRect = new Rectangle<int>(10 + i * 24, 10, 32, 32);
            _renderer.RenderTexture(_heartTextureId, new Rectangle<int>(0, 0, 512, 512), heartRect);
        }

        if (_isGameOver) RenderGameOver();
        if (_isPaused) RenderPaused();

        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);
        _renderer.PresentFrame();
    }



    private void RenderGameOver()
    {
        var (width, height) = _renderer.GetWindowSize();

        int renderWidth = 256;
        int renderHeight = 256;

        var dest = new Rectangle<int>(
            (width - renderWidth) / 2,
            (height - renderHeight) / 2,
            renderWidth,
            renderHeight
        );

        _renderer.RenderTexture(_gameOverTextureId, new Rectangle<int>(0, 0, 2084, 2084), dest);
    }

    private void RenderPaused()
    {
        var (width, height) = _renderer.GetWindowSize();
        _renderer.SetDrawColor(0, 0, 0, 128);
        _renderer.FillRect(new Rectangle<int>(0, 0, width, height));
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null) continue;

            var tempGameObject = (TemporaryGameObject)gameObject!;
            var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
            var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
            if (deltaX < 32 && deltaY < 32)
            {
                _player.LoseLife();
            }
        }

        _player?.Render(_renderer);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null) continue;

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null) continue;

                    var currentTile = _tileIdMap[currentTileId.Value];
                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition() => _player!.Position;

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);
        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");
        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }
}
