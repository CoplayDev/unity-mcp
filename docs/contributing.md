# Contributing to Unity MCP

We welcome contributions to Unity MCP! This guide will help you get started.

## Development Setup

### Prerequisites

- **Unity 2022.3 LTS** or newer
- **Python 3.12+**  
- **Git**
- **Docker** (for container testing)

### Local Setup

1. **Fork and clone:**
   ```bash
   git clone https://github.com/YOUR_USERNAME/unity-mcp.git
   cd unity-mcp
   ```

2. **Install dependencies:**
   ```bash
   pip install -r UnityMcpBridge/UnityMcpServer~/src/requirements-vps.txt
   pip install -r requirements-dev.txt  # Development dependencies
   ```

3. **Set up pre-commit hooks:**
   ```bash
   pre-commit install
   ```

## Code Style

### Python
- **PEP 8** compliance with line length of 100 characters
- **Type hints** for all function parameters and return values
- **Docstrings** for all public functions and classes
- **Black** for code formatting
- **isort** for import organization

### C# (Unity)
- **Unity coding standards**
- **XML documentation** for public APIs
- **Consistent naming conventions**

### Format Code

```bash
# Format Python code
black .
isort .

# Check formatting
black --check .
isort --check-only .
```

## Testing

### Running Tests

```bash
# All tests
python -m pytest

# Specific test file
python -m pytest tests/test_api_compliance.py -v

# With coverage
python -m pytest --cov=UnityMcpBridge --cov-report=html
```

### Test Requirements

- **Unit tests** for all new functionality
- **Integration tests** for API endpoints
- **Security tests** for input validation
- **Performance tests** for critical paths
- **API compliance tests** for specification adherence

### Writing Tests

```python
def test_new_feature():
    """Test description following the pattern: test_<functionality>"""
    # Arrange
    setup_data = create_test_data()
    
    # Act  
    result = function_under_test(setup_data)
    
    # Assert
    assert result.status == "expected_status"
    assert result.data is not None
```

## Pull Request Process

### Before Submitting

1. **Create feature branch:**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Run all checks:**
   ```bash
   # Formatting
   black --check .
   isort --check-only .
   
   # Tests  
   python -m pytest
   
   # Security scan
   bandit -r UnityMcpBridge/
   ```

3. **Update documentation:**
   - Add/update docstrings
   - Update relevant documentation files
   - Add examples if applicable

### Commit Messages

Use conventional commit format:

```
feat: add new Unity asset import tool
fix: resolve scene management memory leak  
docs: update API reference documentation
test: add security validation tests
refactor: improve error handling consistency
```

### PR Description

Include in your PR description:

- **Summary** of changes
- **Motivation** and context
- **Testing** performed
- **Breaking changes** (if any)
- **Related issues** (#123)

## Issue Guidelines

### Bug Reports

Include:
- **Unity version** and platform
- **Python version** 
- **Steps to reproduce**
- **Expected vs actual behavior**
- **Error messages** and logs
- **Minimal example** if possible

### Feature Requests

Include:
- **Use case** and motivation
- **Proposed solution** or approach
- **Alternatives considered**
- **Additional context**

## Security

### Reporting Security Issues

**Do not** create public issues for security vulnerabilities.

Instead:
- Email: security@unity-mcp.dev
- Include detailed description
- Provide reproduction steps
- Allow time for investigation

### Security Guidelines

- **Input validation** for all user inputs
- **Authentication** for all API endpoints
- **Authorization** checks where appropriate
- **Sanitization** of file paths and names
- **Rate limiting** for public endpoints

## Documentation

### Documentation Standards

- **Clear and concise** language
- **Code examples** for complex concepts
- **Step-by-step instructions** for procedures
- **Troubleshooting sections** for common issues
- **Cross-references** to related topics

### Documentation Structure

```
docs/
├── getting-started.md      # New user onboarding
├── development.md          # Development guide
├── production-deployment.md # Production setup
├── docker.md              # Container deployment
├── api-reference.md        # Complete API docs
├── unity-license.md        # Unity licensing
└── contributing.md         # This file
```

## Release Process

### Versioning

We use **Semantic Versioning** (semver):

- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)  
- **PATCH**: Bug fixes (backward compatible)

### Release Checklist

- [ ] All tests passing
- [ ] Documentation updated
- [ ] Version bumped in relevant files
- [ ] Changelog updated
- [ ] Security scan completed
- [ ] Performance benchmarks run
- [ ] Release notes prepared

## Community

### Code of Conduct

- **Be respectful** and inclusive
- **Provide constructive** feedback
- **Focus on** what's best for the community
- **Show empathy** towards other community members

### Communication

- **Discord**: [Join our community](https://discord.gg/y4p8KfzrN4)
- **GitHub Discussions**: For feature discussions
- **GitHub Issues**: For bug reports and feature requests

### Recognition

Contributors are recognized in:
- **CONTRIBUTORS.md** file
- **Release notes** for significant contributions
- **Special mentions** in community updates

## Getting Help

### Development Questions

- **Discord #development** channel
- **GitHub Discussions**
- **Code review feedback**

### Technical Issues

- **GitHub Issues** for bugs
- **Discord #help** for quick questions
- **Documentation** for common scenarios

## Resources

- **Unity Documentation**: [docs.unity3d.com](https://docs.unity3d.com)
- **MCP Specification**: [modelcontextprotocol.io](https://modelcontextprotocol.io)
- **Python Best Practices**: [python.org/dev/peps](https://python.org/dev/peps)
- **Docker Best Practices**: [docs.docker.com/develop/dev-best-practices](https://docs.docker.com/develop/dev-best-practices)

---

Thank you for contributing to Unity MCP! 🚀