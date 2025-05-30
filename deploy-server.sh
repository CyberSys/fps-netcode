#!/usr/bin/env bash

set -u
set -e
set -x

HOST=$1
RPATH=$2

# Since we're not using a docker hub, we'll build an image locally as a tarball
# and simply SCP it up to the server.

# Build the image.
docker build . -t fps-net-server -f Docker/Dockerfile

# Save the image to a tarball and compress it.
docker image save -o /tmp/fps.tar fps-net-server
gzip -f /tmp/fps.tar

# Copy the image to the host.
scp /tmp/fps.tar.gz $HOST:$RPATH

# Start the new container.
ssh $HOST << EOF
  cd $RPATH
  gunzip -f fps.tar.gz
  docker load < fps.tar
EOF

echo "Container still needs to be ran!"

