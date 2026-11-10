FROM node:20-alpine

WORKDIR /app

# Copy dependency specifications
COPY package*.json ./

# Install dependencies
RUN npm install

# Copy source code
COPY . .

# Run build to ensure type safety and bundle verification
RUN npm run build

# Expose Vite development server port
EXPOSE 5173

# Run development server binding to all network interfaces
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "5173"]
