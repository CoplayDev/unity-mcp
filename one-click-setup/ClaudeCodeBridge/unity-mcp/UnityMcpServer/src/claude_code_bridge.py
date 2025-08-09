#!/usr/bin/env python3
"""
Claude Code Bridge for Unity MCP
Provides HTTP API for Claude Code to interact with Unity via Unity MCP Bridge
"""

import logging
import socket
import json
import time
from flask import Flask, request, jsonify
from unity_connection import get_unity_connection

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger('claude-code-bridge')

app = Flask(__name__)

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint"""
    try:
        conn = get_unity_connection()
        # Test connection with ping
        result = conn.send_command("ping")
        return jsonify({
            "status": "healthy",
            "unity_connected": True,
            "message": "Claude Code Bridge is running and connected to Unity"
        })
    except Exception as e:
        return jsonify({
            "status": "degraded",
            "unity_connected": False,
            "error": str(e)
        }), 503

@app.route('/unity/<tool_name>', methods=['GET', 'POST'])
def unity_tool_proxy(tool_name):
    """Proxy requests to Unity MCP tools"""
    try:
        conn = get_unity_connection()
        
        # Build command based on tool and method
        if request.method == 'GET':
            command = {
                "type": tool_name,
                "params": dict(request.args)
            }
        else:  # POST
            command = {
                "type": tool_name,
                "params": request.get_json() or {}
            }
        
        # Send to Unity and get response
        result = conn.send_command(tool_name, command["params"])
        
        return jsonify({
            "status": "success",
            "data": result
        })
            
    except Exception as e:
        logger.error(f"Error in {tool_name}: {str(e)}")
        return jsonify({
            "status": "error",
            "error": str(e),
            "tool": tool_name
        }), 500

if __name__ == '__main__':
    logger.info("Starting Claude Code Bridge for Unity MCP")
    
    # Log available endpoints
    logger.info("Available endpoints:")
    logger.info("  GET  /health - Check connection status")
    logger.info("  GET  /unity/scene - Get scene hierarchy")
    logger.info("  POST /unity/scene - Scene operations")
    logger.info("  GET  /unity/gameobject - List GameObjects")
    logger.info("  POST /unity/gameobject - GameObject operations")
    logger.info("  GET  /unity/console - Read Unity console")
    logger.info("  GET  /unity/editor - Get editor info")
    logger.info("  POST /unity/script - Script operations")
    logger.info("  POST /unity/menu - Execute menu items")
    
    # Start Flask server
    app.run(
        host='0.0.0.0',
        port=6501,
        debug=False,
        use_reloader=False
    )