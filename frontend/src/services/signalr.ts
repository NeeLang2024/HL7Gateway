import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'

export const signalrConnected = ref(false)
export const signalrReconnecting = ref(false)

class SignalRService {
  private connection: signalR.HubConnection
  private started = false

  constructor() {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/hl7monitor', {
        accessTokenFactory: () => localStorage.getItem('hl7_token') || '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    this.connection.onreconnecting(() => {
      signalrConnected.value = false
      signalrReconnecting.value = true
    })

    this.connection.onreconnected(() => {
      signalrConnected.value = true
      signalrReconnecting.value = false
    })

    this.connection.onclose(() => {
      signalrConnected.value = false
      signalrReconnecting.value = false
      this.started = false
    })
  }

  async start(): Promise<void> {
    if (this.started) return
    try {
      await this.connection.start()
      this.started = true
      signalrConnected.value = true
      signalrReconnecting.value = false
    } catch (err) {
      console.error('SignalR connection failed:', err)
      signalrConnected.value = false
    }
  }

  async stop(): Promise<void> {
    if (!this.started) return
    try {
      await this.connection.stop()
    } catch (err) {
      console.error('SignalR stop error:', err)
    } finally {
      this.started = false
      signalrConnected.value = false
      signalrReconnecting.value = false
    }
  }

  onMessageReceived(callback: (...args: any[]) => void): void {
    this.connection.on('MessageReceived', callback)
  }

  onDeviceConnected(callback: (...args: any[]) => void): void {
    this.connection.on('DeviceConnected', callback)
  }

  onDeviceDisconnected(callback: (...args: any[]) => void): void {
    this.connection.on('DeviceDisconnected', callback)
  }

  onAdtSent(callback: (...args: any[]) => void): void {
    this.connection.on('AdtSent', callback)
  }

  off(event: string): void {
    this.connection.off(event)
  }

  get state(): signalR.HubConnectionState {
    return this.connection.state
  }
}

export const signalrService = new SignalRService()
