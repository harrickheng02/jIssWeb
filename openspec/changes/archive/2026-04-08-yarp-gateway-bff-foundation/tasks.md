## 1. Gateway foundation

- [x] 1.1 Create a YARP gateway project with basic ASP.NET Core host setup
- [x] 1.2 Add route and cluster configuration for at least one downstream business service
- [x] 1.3 Ensure gateway forwards authentication headers and preserves downstream response behavior

## 2. BFF foundation

- [x] 2.1 Create an ASP.NET Core BFF project or equivalent module for frontend-facing orchestration
- [x] 2.2 Add at least one sample BFF endpoint that calls downstream services and returns a frontend-shaped response
- [x] 2.3 Define the boundary between direct gateway forwarding routes and BFF aggregation routes

## 3. Frontend integration

- [x] 3.1 Update frontend API client configuration to use a unified backend entry instead of long-term dependence on per-service prefixes
- [x] 3.2 Verify authenticated requests still attach Bearer tokens through the unified entry
- [x] 3.3 Keep a short-term compatibility path or migration note for existing direct service prefixes if needed

## 4. Docker and connectivity

- [x] 4.1 Update `docker-compose.yml` or related compose structure to include gateway-tier services or explicit placeholders
- [x] 4.2 Add `.env.example` or equivalent variables for gateway and BFF ports plus downstream URLs
- [x] 4.3 Document host-run versus container-network routing for Nginx, YARP, BFF, and downstream services

## 5. Verification

- [x] 5.1 Verify local routing path from frontend to gateway or BFF to at least one downstream service
- [x] 5.2 Verify Docker-network connectivity assumptions for service-name routing
