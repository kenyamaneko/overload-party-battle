#!/usr/bin/env bash
# Build the battle container image and push it to Artifact Registry.
# Called from .github/workflows/ci.yaml on push to main.
set -euo pipefail

: "${REGISTRY:?REGISTRY env required}"
: "${PROJECT_ID:?PROJECT_ID env required}"
: "${REPOSITORY:?REPOSITORY env required}"
: "${IMAGE_NAME:?IMAGE_NAME env required}"
: "${IMAGE_TAG:?IMAGE_TAG env required}"

IMAGE="${REGISTRY}/${PROJECT_ID}/${REPOSITORY}/${IMAGE_NAME}"

echo "::group::docker build ${IMAGE}:${IMAGE_TAG}"
# Cloudsmith repos are public; the Dockerfile's `dotnet restore` reads
# OverloadParty.GameDesignConstants anonymously over the configured feed in
# nuget.config. No auth secret is forwarded into the build context.
docker build -t "${IMAGE}:${IMAGE_TAG}" -t "${IMAGE}:latest" .
echo "::endgroup::"

docker push "${IMAGE}:${IMAGE_TAG}"
docker push "${IMAGE}:latest"
