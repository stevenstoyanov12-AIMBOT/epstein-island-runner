# Cloud streaming image for Vast.ai cards: Linux build + coturn + supervisor.
#   docker build -t <dockerhub-user>/museum-cloud:latest -f cloud/Dockerfile .    (run from MuseumGame/, after Build > Cloud Streaming)
#   docker push <dockerhub-user>/museum-cloud:latest
# No secrets in here: CLOUD_KEY / CLOUD_REGION come from the Vast template's env vars.
FROM nvidia/cuda:12.4.1-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive \
    NVIDIA_DRIVER_CAPABILITIES=all \
    NVIDIA_VISIBLE_DEVICES=all

RUN apt-get update && apt-get install -y --no-install-recommends \
        libvulkan1 vulkan-tools xvfb coturn python3 ca-certificates curl \
        libgl1 libglu1-mesa libxcursor1 libxrandr2 libxinerama1 libxi6 libasound2 libpulse0 \
    && rm -rf /var/lib/apt/lists/*

# NVIDIA's Vulkan driver is mounted by the container toolkit; point the loader at it
RUN mkdir -p /usr/share/vulkan/icd.d && \
    echo '{"file_format_version":"1.0.0","ICD":{"library_path":"libGLX_nvidia.so.0","api_version":"1.3"}}' \
        > /usr/share/vulkan/icd.d/nvidia_icd.json

WORKDIR /app
COPY CloudBuild/ /app/game/
COPY cloud/supervisor.py /app/supervisor.py
RUN chmod +x /app/game/Museum.x86_64

# 3478 udp+tcp = TURN (the only ports players reach; map them as Vast direct ports)
EXPOSE 3478/udp 3478/tcp
CMD ["python3", "/app/supervisor.py"]
