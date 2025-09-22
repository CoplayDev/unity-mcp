# Development Guide

This guide covers local development setup and advanced Unity MCP usage.

## Development Setup

### 1. Clone Repository

```bash
git clone https://github.com/CoplayDev/unity-mcp.git
cd unity-mcp
```

### 2. Install Dependencies

```bash
pip install -r UnityMcpBridge/UnityMcpServer~/src/requirements.txt
```

### 3. Unity Editor Setup

1. Open Unity Hub
2. Create new 3D project or open existing project
3. Install Unity MCP package (see [Getting Started](getting-started.md))

### 4. Development Environment

For development, you can run the MCP server directly:

```bash
cd UnityMcpBridge/UnityMcpServer~/src
python server.py
```

## Unity MCP Tools

Unity MCP provides comprehensive tools for Unity automation:

### Asset Management
- **Import assets** from files or URLs
- **Organize asset hierarchy** and folders
- **Configure import settings** for different asset types
- **Search and filter** assets by type and properties

### Scene Management  
- **Create and manage GameObjects** with hierarchies
- **Configure components** and their properties
- **Manage lighting** and environment settings
- **Control cameras** and scene composition

### Script Generation
- **Generate C# scripts** for Unity components
- **Attach scripts** to GameObjects automatically
- **Configure script parameters** and references
- **Implement common Unity patterns** (MonoBehaviour, ScriptableObject)

### Build Configuration
- **Configure build settings** for different platforms
- **Manage build pipeline** and automation
- **Set up deployment** workflows
- **Handle build optimization** settings

## Example Workflows

### Creating a Simple Game

Ask your AI assistant:

> "Create a simple endless runner game with a player character that can jump over obstacles"

Unity MCP will:
1. Set up the scene with appropriate lighting
2. Create player GameObject with movement script
3. Generate obstacle prefabs with collision detection
4. Implement game manager with score system
5. Configure UI elements for game feedback

### Asset Import and Setup

> "Import these texture files and set up materials for a 3D environment"

Unity MCP will:
1. Import textures with optimal settings
2. Create materials with appropriate shaders
3. Organize assets in logical folder structure
4. Apply materials to provided 3D models

### Rapid Prototyping

> "Create a physics-based puzzle game prototype with moveable blocks"

Unity MCP will:
1. Set up physics environment
2. Create interactable block prefabs
3. Implement player interaction system
4. Add basic game rules and win conditions

## Advanced Usage

### Custom Tool Integration

You can extend Unity MCP with custom tools:

```python
@unity_mcp.tool("custom_tool")
def my_custom_tool(parameter: str) -> str:
    """Custom tool description"""
    # Your implementation
    return result
```

### Batch Operations

Unity MCP supports batch operations for efficiency:

> "Create 10 different enemy variants with random properties and attach unique AI scripts to each"

### Project Templates

Create project templates for common game types:

> "Set up a 2D platformer template with player controller, tilemap system, and basic enemies"

## Testing

### Running Tests

```bash
# Unit tests
python -m pytest tests/ -v

# Integration tests  
python -m pytest tests/integration/ -v

# API compliance tests
python -m pytest tests/test_api_compliance.py -v
```

### Manual Testing

Test Unity MCP functionality:

1. **Basic connectivity**: Ask for scene information
2. **Asset operations**: Import and manage assets
3. **GameObject creation**: Create and configure objects
4. **Script generation**: Generate and attach scripts
5. **Build operations**: Configure and test builds

## Troubleshooting

### Development Issues

**Unity MCP tools not working:**
- Check Unity Editor is open and responsive
- Verify project is properly loaded
- Check console for Unity errors

**Performance issues:**
- Monitor Unity Editor memory usage
- Check for leaked GameObjects
- Optimize asset import settings

**Script generation errors:**
- Verify Unity project compilation state
- Check for existing script conflicts
- Review generated script syntax

### Debugging

Enable debug logging:

```bash
export UNITY_MCP_DEBUG=1
python server.py
```

Monitor Unity Console for detailed operation logs.

## Contributing

See [Contributing Guide](contributing.md) for:
- Code style guidelines
- Testing requirements
- Pull request process
- Development workflow

## Next Steps

- [API Reference](api-reference.md) - Complete tool documentation
- [Production Deployment](production-deployment.md) - Deploy Unity Build Service
- [Docker Guide](docker.md) - Container deployment options