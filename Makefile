# Zharfa — Docker Compose shortcuts.
# Thin wrappers over deploy/zharfa.sh so the stack can be driven with make.
# The helper keeps its own env checks and includes docker-compose.9router.yml
# automatically when that file exists.

.PHONY: up down

# Build (if needed) and start the stack in the background.
up:
	./deploy/zharfa.sh up

# Stop the stack; the database volume is kept.
down:
	./deploy/zharfa.sh down
