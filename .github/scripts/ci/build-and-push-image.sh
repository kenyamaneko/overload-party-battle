#!/usr/bin/env bash
# battle のコンテナイメージをビルドし Artifact Registry に push する。
set -euo pipefail

: "${REGISTRY:?REGISTRY env required}"
: "${PROJECT_ID:?PROJECT_ID env required}"
: "${REPOSITORY:?REPOSITORY env required}"
: "${IMAGE_NAME:?IMAGE_NAME env required}"
: "${IMAGE_TAG:?IMAGE_TAG env required}"

IMAGE="${REGISTRY}/${PROJECT_ID}/${REPOSITORY}/${IMAGE_NAME}"

echo "::group::docker build ${IMAGE}:${IMAGE_TAG}"
# Cloudsmith は public repo のため、Dockerfile 内で匿名登録した feed に対して
# dotnet restore が認証なしでアクセスする。ビルドコンテキストに secret は不要。
EPOCH=$(date +%s)
docker build -t "${IMAGE}:${EPOCH}-${IMAGE_TAG}" -t "${IMAGE}:${IMAGE_TAG}" -t "${IMAGE}:latest" .
echo "::endgroup::"

docker push "${IMAGE}:${EPOCH}-${IMAGE_TAG}"
docker push "${IMAGE}:${IMAGE_TAG}"
docker push "${IMAGE}:latest"
