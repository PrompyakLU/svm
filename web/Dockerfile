# SVM Web — build Angular + serve ด้วย nginx. linux/amd64 for AWS Fargate.
# ยังไม่ deploy จริง — ใช้รัน local ด้วย docker compose / ทดสอบ image ได้

# ---- build stage ----
FROM node:24-alpine AS build
WORKDIR /app

# cache layer ของ npm ci ด้วย lockfile ก่อน
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# build production (defaultConfiguration = production → environment.production.ts, apiBaseUrl=/api/v1)
RUN npm run build

# ---- runtime stage ----
FROM nginx:1.27-alpine AS runtime

# config ของเราแทน default
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/svm.conf

# Angular output: dist/web/browser
COPY --from=build /app/dist/web/browser /usr/share/nginx/html

EXPOSE 8080
# nginx:alpine รันเป็น non-root ได้ผ่าน user nginx; ฟังที่ 8080 (ตรงกับ nginx.conf)
CMD ["nginx", "-g", "daemon off;"]
