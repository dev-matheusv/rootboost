import axios from 'axios';
import { readSession } from './session';

// Em dev, a API local; na Vercel, VITE_API_BASE_URL aponta pro Railway.
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  timeout: 20_000,
});

api.interceptors.request.use((config) => {
  const session = readSession();
  if (session) config.headers.Authorization = `Bearer ${session.token}`;
  return config;
});
