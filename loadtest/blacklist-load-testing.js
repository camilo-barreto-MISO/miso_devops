import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

const API_BASE_URL = 'http://lb-app-blacklistapi-2033585434.us-east-1.elb.amazonaws.com' // 'lb-app-blacklistapi-2033585434.us-east-1.elb.amazonaws.com';
const API_KEY = '123456';

export const options = {
  stages: [
    { duration: '30s', target: 2000 }, // Ramp up to 200 users over 30 seconds
    { duration: '1m', target: 2000 },  // Stay at 200 users for 1 minute
    { duration: '30s', target: 0 },  // Ramp down to 0 users over 30 seconds
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'], // 95% of requests should complete within 2s
    'http_req_failed{scenario:success}': ['rate<0.01'], // Less than 1% failure rate for success scenarios
  },
};

const headers = {
  'Content-Type': 'application/json',
  'X-Api-Key': API_KEY,
};

// Generate a unique email for each iteration
function generateUniqueEmail() {
  return `test.${randomString(10)}@example.com`;
}

export default function () {
  const testEmail = generateUniqueEmail();

  // Group 1: Add email to blacklist (success case)
  {
    const payload = JSON.stringify({
      email: testEmail,
      blocked_reason: 'Test',
      app_uuid: '479d2bd7-3c95-4ea5-bb0c-fe394fa7a7e0'
    });

    const res = http.post(`${API_BASE_URL}/blacklist`, payload, { headers, tags: { scenario: 'success' } });
    
    check(res, {
      'Add email status is 201': (r) => r.status === 201,
      'Response has correct message': (r) => {
        try {
          const body = JSON.parse(r.body);
          return body.message === 'Email added to the blacklist';
        } catch (e) {
          return false;
        }
      },
    });

    sleep(1);
  }

  // Group 2: Get email from blacklist
  {
    const res = http.get(`${API_BASE_URL}/blacklist/${testEmail}`, { headers, tags: { scenario: 'success' } });
    
    check(res, {
      'Get email status is 200': (r) => r.status === 200,
      'Response includes email data': (r) => {
        try {
          const body = JSON.parse(r.body);
          return body && typeof body === 'object';
        } catch (e) {
          return false;
        }
      },
    });

    sleep(1);
  }

  // Group 3: Test authentication failure
  {
    const invalidHeaders = {
      'Content-Type': 'application/json',
      'X-Api-Key': 'invalid-key'
    };

    const payload = JSON.stringify({
      email: generateUniqueEmail(),
      blocked_reason: 'Test',
      app_uuid: '479d2bd7-3c95-4ea5-bb0c-fe394fa7a7e0'
    });

    const res = http.post(`${API_BASE_URL}/blacklist`, payload, { headers: invalidHeaders, tags: { scenario: 'auth_failure' } });
    
    check(res, {
      'Unauthorized status is 401': (r) => r.status === 401,
    });

    sleep(1);
  }

  // Group 4: Test validation errors
  {
    // Test missing email
    const invalidPayload1 = JSON.stringify({
      blocked_reason: 'Test',
      app_uuid: '479d2bd7-3c95-4ea5-bb0c-fe394fa7a7e0'
    });

    const res1 = http.post(`${API_BASE_URL}/blacklist`, invalidPayload1, { headers, tags: { scenario: 'validation' } });
    
    check(res1, {
      'Missing email status is 400': (r) => r.status === 400,
    });

    // Test missing app_uuid
    const invalidPayload2 = JSON.stringify({
      email: generateUniqueEmail(),
      blocked_reason: 'Test'
    });

    const res2 = http.post(`${API_BASE_URL}/blacklist`, invalidPayload2, { headers, tags: { scenario: 'validation' } });
    
    check(res2, {
      'Missing app_uuid status is 400': (r) => r.status === 400,
    });

    // Test too long blocked_reason
    const longReason = 'x'.repeat(300);
    const invalidPayload3 = JSON.stringify({
      email: generateUniqueEmail(),
      blocked_reason: longReason,
      app_uuid: '479d2bd7-3c95-4ea5-bb0c-fe394fa7a7e0'
    });

    const res3 = http.post(`${API_BASE_URL}/blacklist`, invalidPayload3, { headers, tags: { scenario: 'validation' } });
    
    check(res3, {
      'Too long reason status is 400': (r) => r.status === 400,
    });

    sleep(1);
  }
}