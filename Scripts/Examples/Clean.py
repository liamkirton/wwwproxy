# --------------------------------------------------------------------------------
# WwwProxy\Scripts\Clean.py
#
# Copyright ©2008 Liam Kirton <liam@int3.ws>
# --------------------------------------------------------------------------------
# Clean.py
#
# Created: 09/04/2008
# --------------------------------------------------------------------------------

# Parameter Types

# WwwProxy.ProxyRequest
# .Id
# .Pass
# .Skip
# .Header
# .Data

# WwwProxy.ProxyResponse
# .Id
# .Completable
# .Pass
# .Skip
# .Header
# .Contents
	
# --------------------------------------------------------------------------------

import clr
clr.AddReference('WwwProxy')
from WwwProxy import *

# --------------------------------------------------------------------------------

class WwwProxyFilter(object):

	# ----------------------------------------------------------------------------
	
	def __init__(self):
		pass

	# ----------------------------------------------------------------------------
	
	def pre_request_filter(self, request):
		print '>>>>> pre_request_filter'

	# ----------------------------------------------------------------------------

	def post_request_filter(self, request):
		print '>>>>> post_request_filter'
	
	# ----------------------------------------------------------------------------
		
	def pre_response_filter(self, request, response):
		print '<<<<< pre_response_filter'

	# ----------------------------------------------------------------------------
	
	def post_response_filter(self, request, response):
		print '<<<<< post_response_filter'

	# ----------------------------------------------------------------------------

# --------------------------------------------------------------------------------
