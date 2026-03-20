import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Layout from './components/Layout'
import ChannelsPage from './pages/ChannelsPage'
import VodsPage from './pages/VodsPage'

export default function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/"     element={<ChannelsPage />} />
          <Route path="/vods" element={<VodsPage />} />
          <Route path="*"    element={<Navigate to="/" replace />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
