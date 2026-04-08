## ADDED Requirements

### Requirement: Document ingress chain connectivity

The architecture documentation and configuration SHALL describe how requests traverse the ingress chain from Nginx to YARP, from YARP to BFF or downstream services, and from BFF to business services in both host-run and container-network scenarios.

#### Scenario: Developer understands each hop
- **WHEN** a developer configures the stack locally or in Docker
- **THEN** they SHALL be able to identify the expected hostnames, ports, and routing hop for Nginx, YARP, BFF, and downstream services

### Requirement: Service-name routing in container mode

When the gateway, BFF, and downstream services run inside the same Compose network, their route and upstream configuration SHALL use service names and internal ports rather than localhost-based assumptions.

#### Scenario: In-network gateway resolves service names
- **WHEN** the architecture stack runs fully inside Compose
- **THEN** YARP and BFF upstream targets SHALL reference Compose service names for cross-container communication
