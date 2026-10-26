FROM node:24-alpine
WORKDIR /app
COPY package*.json ./
RUN apk add --no-cache git \
  && git config --global url."https://github.com/".insteadOf "ssh://git@github.com/" \
  && git config --global url."https://github.com/".insteadOf "git@github.com:" \
  && npm ci --omit=dev \
  && apk del git \
  && rm -f /root/.gitconfig
COPY src ./src
RUN mkdir -p /app/data && chown node:node /app/data
USER node
ENV HOST=0.0.0.0 PORT=5091 WHATSAPP_DATA_DIR=/app/data
EXPOSE 5091
CMD ["node", "src/server.mjs"]
