
# This utility strips carriage return characters (\r) from bytes or strings 
class CRStripper:
    def __init__(self, stream):
        self._stream = stream
    
    def write(self, data):
        if isinstance(data, bytes):
            return self._stream.write(data.replace(b'\r', b''))
        if isinstance(data, str):
            return self._stream.write(data.replace('\r', ''))
        return self._stream.write(data)
        
    def flush(self):
        return self._stream.flush()
        
    def __getattr__(self, name):
        return getattr(self._stream, name)
