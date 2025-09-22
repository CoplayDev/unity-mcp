import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// Custom metrics
const healthChecks = new Counter('health_checks');
const commandExecutions = new Counter('command_executions');
const errorRate = new Rate('errors');
const commandDuration = new Trend('command_duration');

// Test configuration
export const options = {
  stages: [
    // Ramp-up: gradually increase to 10 virtual users over 2 minutes
    { duration: '2m', target: 10 },
    // Stay at 10 users for 5 minutes (sustained load)
    { duration: '5m', target: 10 },
    // Ramp-up to 20 users over 2 minutes (peak load)
    { duration: '2m', target: 20 },
    // Stay at 20 users for 3 minutes
    { duration: '3m', target: 20 },
    // Ramp down to 5 users over 2 minutes
    { duration: '2m', target: 5 },
    // Stay at 5 users for 2 minutes (cool down)
    { duration: '2m', target: 5 },
    // Ramp down to 0
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<5000'], // 95% of requests under 5s
    http_req_failed: ['rate<0.05'],    // Less than 5% error rate
    errors: ['rate<0.1'],              // Less than 10% errors
    command_duration: ['p(90)<3000'],  // 90% of commands under 3s
  },
};

// Environment variables
const BASE_URL = __ENV.UNITY_MCP_URL || 'http://unity-mcp-server.unity-mcp.svc.cluster.local';
const HEALTH_ENDPOINT = `${BASE_URL}/health`;
const READY_ENDPOINT = `${BASE_URL}/ready`;
const EXECUTE_ENDPOINT = `${BASE_URL}/execute-command`;

// Unity command templates for realistic testing
const UNITY_COMMANDS = [
  {
    action: 'create_gameobject',
    params: {
      name: `TestObject_${Math.random().toString(36).substr(2, 9)}`,
      position: { x: Math.random() * 10, y: Math.random() * 10, z: Math.random() * 10 }
    }
  },
  {
    action: 'create_scene',
    params: {
      sceneName: `TestScene_${Math.random().toString(36).substr(2, 9)}`
    }
  },
  {
    action: 'load_scene',
    params: {
      sceneName: 'SampleScene'
    }
  },
  {
    action: 'manage_asset',
    params: {
      operation: 'create',
      assetType: 'material',
      name: `TestMaterial_${Math.random().toString(36).substr(2, 9)}`
    }
  },
  {
    action: 'execute_menu_item',
    params: {
      menuPath: 'Assets/Create/Material'
    }
  }
];

// Helper function to get random command
function getRandomCommand() {
  return UNITY_COMMANDS[Math.floor(Math.random() * UNITY_COMMANDS.length)];
}

// Health check function
function performHealthCheck() {
  const response = http.get(HEALTH_ENDPOINT, {
    timeout: '10s',
    tags: { endpoint: 'health' }
  });
  
  healthChecks.add(1);
  
  const success = check(response, {
    'health status is 200': (r) => r.status === 200,
    'health response has status': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.status === 'healthy';
      } catch {
        return false;
      }
    }
  });
  
  if (!success) {
    errorRate.add(1);
    console.error(`Health check failed: ${response.status} ${response.body}`);
  }
  
  return success;
}

// Readiness check function
function performReadinessCheck() {
  const response = http.get(READY_ENDPOINT, {
    timeout: '10s',
    tags: { endpoint: 'ready' }
  });
  
  const success = check(response, {
    'readiness status is 200': (r) => r.status === 200,
    'readiness response is valid': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.server_ready === true;
      } catch {
        return false;
      }
    }
  });
  
  if (!success) {
    errorRate.add(1);
  }
  
  return success;
}

// Command execution function
function executeUnityCommand() {
  const command = getRandomCommand();
  const startTime = Date.now();
  
  const response = http.post(EXECUTE_ENDPOINT, JSON.stringify(command), {
    headers: {
      'Content-Type': 'application/json',
    },
    timeout: '30s',
    tags: { endpoint: 'execute', action: command.action }
  });
  
  const duration = Date.now() - startTime;
  commandDuration.add(duration);
  commandExecutions.add(1);
  
  const success = check(response, {
    'command status is 200': (r) => r.status === 200,
    'command response is valid': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.status === 'completed' && body.result && body.result.success === true;
      } catch {
        return false;
      }
    },
    'command completed in reasonable time': () => duration < 30000
  });
  
  if (!success) {
    errorRate.add(1);
    console.error(`Command execution failed: ${command.action}, Status: ${response.status}, Duration: ${duration}ms`);
    if (response.body) {
      console.error(`Response body: ${response.body.substr(0, 200)}`);
    }
  }
  
  return { success, duration, action: command.action };
}

// Main test scenario
export default function() {
  // Perform health check (10% of the time)
  if (Math.random() < 0.1) {
    performHealthCheck();
    sleep(1);
    return;
  }
  
  // Perform readiness check (5% of the time)
  if (Math.random() < 0.05) {
    performReadinessCheck();
    sleep(1);
    return;
  }
  
  // Execute Unity command (85% of the time)
  const result = executeUnityCommand();
  
  // Add realistic delay based on command type
  let sleepTime = 2; // Default 2 seconds
  
  switch (result.action) {
    case 'create_scene':
    case 'load_scene':
      sleepTime = 3; // Scene operations take longer
      break;
    case 'create_gameobject':
    case 'manage_asset':
      sleepTime = 1; // Quick operations
      break;
    default:
      sleepTime = 2;
  }
  
  sleep(sleepTime + Math.random() * 2); // Add some randomness
}

// Setup function - runs once at the beginning
export function setup() {
  console.log(`Starting load test against ${BASE_URL}`);
  
  // Verify the service is accessible
  const healthResponse = http.get(HEALTH_ENDPOINT, { timeout: '30s' });
  
  if (healthResponse.status !== 200) {
    throw new Error(`Service not accessible: ${healthResponse.status} ${healthResponse.body}`);
  }
  
  console.log('Service is accessible, starting test...');
  
  return {
    baseUrl: BASE_URL,
    startTime: Date.now()
  };
}

// Teardown function - runs once at the end
export function teardown(data) {
  console.log(`Load test completed. Duration: ${(Date.now() - data.startTime) / 1000}s`);
  console.log(`Target URL: ${data.baseUrl}`);
}